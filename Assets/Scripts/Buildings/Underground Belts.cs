using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Factorio-style underground belt. Two pieces form a pair: an ENTRANCE and an EXIT.
///
///  - Place a piece, then place another one facing the SAME direction in front of it (up to
///    maxDistance cells away). The second piece automatically becomes the exit.
///  - The entrance takes items from belts behind/beside it (never from the cell it faces).
///    Items disappear underground, travel at belt speed and come out of the exit, which pushes
///    them into whatever is in front of it. Anything can be built in between.
///  - Pairing = nearest unpaired entrance behind an exit. Pairs may be nested, never interleaved.
///  - Demolishing one piece leaves the other as an orphan (a red dot shows it is unpaired);
///    placing a new piece in range pairs it again. Rotating a piece breaks its pair.
///  - Throughput equals a belt of the same speedMultiplier.
///
/// Save format (no SaveData changes needed): BuildingSave.crafting = "is exit",
/// laneItems / laneProgress = items travelling underground (stored on the exit).
/// </summary>
public class UndergroundBelt : FactoryBuilding
{
    public enum Role { Entrance, Exit }

    [Min(2), Tooltip("Furthest exit, in cells from the entrance (5 = up to 4 cells between them, like Factorio's basic underground).")]
    public int maxDistance = 5;

    private struct Transit
    {
        public ItemDefinition def;
        public float time; // seconds until the item may come out
    }

    private Role role = Role.Entrance;
    private bool roleResolved;
    private UndergroundBelt partner;
    private readonly List<Transit> transit = new List<Transit>();

    private int shownKey = -1;
    private Transform tunnel;

    public Role CurrentRole { get { return role; } }

    /// <summary>True when both pieces point at each other.</summary>
    public bool IsPaired { get { return partner != null && partner.partner == this; } }

    // Only entrances receive, only exits send (so belts, splitters and inserters treat each piece correctly).
    public override bool HasInput => role == Role.Entrance;
    public override bool HasOutput => role == Role.Exit;

    // Items cross the tile from its back edge to its front edge, like on a belt.
    protected override Vector3 EntryLocalOffset
    {
        get
        {
            float cs = GridManager.Instance != null ? GridManager.Instance.cellSize : 1f;
            return -(Vector3)(Vector2)DirectionUtil.ToVector(Facing) * (0.5f * cs);
        }
    }

    // ---------------------------------------------------------------
    // Role + pairing
    // ---------------------------------------------------------------

    void Start()
    {
        if (roleResolved) return; // restored from a save: role already known
        roleResolved = true;

        // A newly placed piece becomes the exit if an unpaired entrance is waiting behind it.
        role = Role.Exit;
        UndergroundBelt entrance = FindUnpaired(-1, Role.Entrance);
        if (entrance != null && entrance.FindUnpaired(+1, Role.Exit) == this) Link(entrance, this);
        else role = Role.Entrance;
    }

    /// <summary>
    /// Nearest same-facing piece along the axis that is not yet paired (paired pieces are skipped:
    /// the tunnel simply passes under them). Returns it only if it has the wanted role.
    /// sign: +1 = ahead of this piece, -1 = behind it.
    /// </summary>
    private UndergroundBelt FindUnpaired(int sign, Role want)
    {
        var grid = GridManager.Instance;
        Vector2Int step = DirectionUtil.ToVector(Facing) * sign;
        for (int d = 1; d <= maxDistance; d++)
        {
            var b = grid.GetBuilding(GridPosition + step * d) as UndergroundBelt;
            if (b == null || b.Facing != Facing || b.IsPaired) continue;
            return b.role == want ? b : null;
        }
        return null;
    }

    private static void Link(UndergroundBelt a, UndergroundBelt b)
    {
        a.partner = b;
        b.partner = a;
    }

    private void Unlink()
    {
        if (partner != null && partner.partner == this) partner.partner = null;
        partner = null;
    }

    private int PartnerDistance()
    {
        if (partner == null) return 0;
        Vector2Int d = partner.GridPosition - GridPosition;
        return Mathf.Abs(d.x) + Mathf.Abs(d.y);
    }

    private bool PairStillValid()
    {
        if (partner == null || partner.partner != this) return false;
        if (partner.Facing != Facing || partner.role == role) return false;

        Vector2Int f = DirectionUtil.ToVector(Facing);
        Vector2Int d = partner.GridPosition - GridPosition;
        int along = d.x * f.x + d.y * f.y;
        if (d != f * along) return false; // not on the same axis

        int dist = role == Role.Entrance ? along : -along;
        return dist >= 1 && dist <= Mathf.Min(maxDistance, partner.maxDistance);
    }

    private void ValidateOrLink()
    {
        if (partner != null && !PairStillValid()) Unlink();
        if (IsPaired) return;

        // Both sides must agree they are each other's nearest candidate.
        if (role == Role.Entrance)
        {
            UndergroundBelt exit = FindUnpaired(+1, Role.Exit);
            if (exit != null && exit.FindUnpaired(-1, Role.Entrance) == this) Link(this, exit);
        }
        else
        {
            UndergroundBelt entrance = FindUnpaired(-1, Role.Entrance);
            if (entrance != null && entrance.FindUnpaired(+1, Role.Exit) == this) Link(entrance, this);
        }
    }

    // ---------------------------------------------------------------
    // Item flow
    // ---------------------------------------------------------------

    /// <summary>Like a belt, an entrance never takes items from the cell it faces. Exits take nothing.</summary>
    public override bool AcceptsFrom(FactoryBuilding source)
    {
        if (role != Role.Entrance) return false;
        Vector2Int front = GridPosition + DirectionUtil.ToVector(Facing);
        foreach (var c in source.Cells)
            if (c == front) return false;
        return true;
    }

    public override bool WouldAccept(ItemDefinition def)
    {
        return role == Role.Entrance && IsPaired && heldItem == null;
    }

    public override bool TryAcceptInput(ItemVisual item)
    {
        // An entrance without an exit refuses items, so the belt behind it just waits.
        if (role != Role.Entrance || !IsPaired) return false;
        return base.TryAcceptInput(item);
    }

    protected override void Update()
    {
        if (GridManager.Instance != null) ValidateOrLink();

        base.Update(); // moves the item across this tile and calls TryPushOutput

        DrainTransit();

        if (GridManager.Instance != null)
        {
            UpdateVisualsIfChanged();
            UpdateTunnelStrip();
        }
    }

    protected override void TryPushOutput()
    {
        if (role == Role.Exit)
        {
            base.TryPushOutput(); // normal push into the cell in front
            return;
        }

        // Entrance: the item goes underground instead of into the cell in front.
        if (heldItem == null || !IsPaired || !partner.CanReceiveUnderground()) return;

        int gap = Mathf.Max(0, PartnerDistance() - 1); // cells between the two pieces
        float speed = Mathf.Max(0.01f, itemTravelSpeed * SpeedMultiplier);
        float delay = gap / speed;

        ItemDefinition def = heldItem.Definition;
        heldItem.Release();
        heldItem = null;
        moveProgress = 0f;

        partner.transit.Add(new Transit { def = def, time = delay });
    }

    private bool CanReceiveUnderground()
    {
        return role == Role.Exit && transit.Count < Mathf.Max(1, PartnerDistance()) + 1;
    }

    /// <summary>Exit side: counts down the travel time and lets items come out in order.</summary>
    private void DrainTransit()
    {
        if (transit.Count == 0) return;

        float dt = Time.deltaTime;
        for (int i = 0; i < transit.Count; i++)
        {
            Transit t = transit[i];
            t.time = Mathf.Max(0f, t.time - dt);
            transit[i] = t;
        }

        if (heldItem == null && transit[0].time <= 0f)
        {
            heldItem = ItemVisual.Spawn(transit[0].def, transform.position + EntryLocalOffset);
            moveProgress = 0f;
            transit.RemoveAt(0);
        }
    }

    // ---------------------------------------------------------------
    // Visuals
    // ---------------------------------------------------------------

    protected override Sprite GetOverrideSprite()
    {
        Color tint = Definition != null ? Definition.iconColor : new Color(0.85f, 0.6f, 0.12f);
        return UndergroundSprites.Get(tint, role, Facing, IsPaired);
    }

    private void UpdateVisualsIfChanged()
    {
        int key = (int)role | ((int)Facing << 1) | ((IsPaired ? 1 : 0) << 3);
        if (key == shownKey) return;
        shownKey = key;
        RefreshVisuals();
    }

    /// <summary>A faint strip on the ground between the two pieces so you can see where the tunnel runs.</summary>
    private void UpdateTunnelStrip()
    {
        int dist = PartnerDistance();
        bool show = role == Role.Entrance && IsPaired && dist > 1;
        if (!show)
        {
            if (tunnel != null) tunnel.gameObject.SetActive(false);
            return;
        }

        if (tunnel == null)
        {
            var go = new GameObject("Tunnel");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SquareSpriteFactory.GetSquareSprite();
            sr.color = new Color(0f, 0f, 0f, 0.3f);
            sr.sortingOrder = 0;
            tunnel = go.transform;
        }

        float cs = GridManager.Instance.cellSize;
        Vector2Int f = DirectionUtil.ToVector(Facing);
        float gapLen = (dist - 1) * cs;
        float thick = 0.3f * cs;

        tunnel.gameObject.SetActive(true);
        tunnel.localPosition = new Vector3(f.x, f.y, 0f) * (dist * 0.5f * cs);
        tunnel.localScale = f.x != 0 ? new Vector3(gapLen, thick, 1f) : new Vector3(thick, gapLen, 1f);
    }

    // ---------------------------------------------------------------
    // Save / load / demolish
    // ---------------------------------------------------------------

    public override void CaptureState(BuildingSave s)
    {
        base.CaptureState(s);
        s.crafting = role == Role.Exit; // reused flag: "this piece is the exit"
        foreach (var t in transit)
        {
            if (t.def == null) continue;
            s.laneItems.Add(t.def.SaveKey);
            s.laneProgress.Add(t.time);
        }
    }

    public override void RestoreState(BuildingSave s, Func<string, ItemDefinition> findItem)
    {
        base.RestoreState(s, findItem);

        roleResolved = true;
        role = s.crafting ? Role.Exit : Role.Entrance;
        partner = null; // pairs are rebuilt from positions on the next frames
        transit.Clear();

        if (s.laneItems == null || s.laneProgress == null) return;
        for (int i = 0; i < s.laneItems.Count && i < s.laneProgress.Count; i++)
        {
            ItemDefinition def = findItem(s.laneItems[i]);
            if (def != null) transit.Add(new Transit { def = def, time = Mathf.Max(0f, s.laneProgress[i]) });
        }
    }

    public override void Demolish()
    {
        Unlink();     // the other piece becomes an orphan
        transit.Clear();
        base.Demolish();
    }
}

/// <summary>
/// Draws the underground belt art at runtime (one small texture per role / facing / paired state),
/// the same way SquareSpriteFactory and ConveyorArrowSprites avoid needing art files.
/// Belt lane with chevrons on one half, dark tunnel hood on the other.
/// </summary>
public static class UndergroundSprites
{
    private const int N = 32;
    private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { cache.Clear(); }

    public static Sprite Get(Color tint, UndergroundBelt.Role role, Direction facing, bool paired)
    {
        string key = ColorUtility.ToHtmlStringRGB(tint) + "|" + (int)role + "|" + (int)facing + "|" + paired;
        if (cache.TryGetValue(key, out Sprite s) && s != null) return s;

        s = Build(tint, role, facing, paired);
        cache[key] = s;
        return s;
    }

    private static Color Mul(Color c, float k) { return new Color(c.r * k, c.g * k, c.b * k, 1f); }

    private static Sprite Build(Color tint, UndergroundBelt.Role role, Direction facing, bool paired)
    {
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        Vector2Int f = DirectionUtil.ToVector(facing);
        Vector2 l = new Vector2(-f.y, f.x);

        Color lane = Mul(tint, 0.8f);
        Color chevron = Color.Lerp(tint, Color.white, 0.75f);
        Color hood = Mul(tint, 0.35f);
        Color rim = Mul(tint, 0.65f);
        Color opening = new Color(0.05f, 0.04f, 0.04f, 1f);
        Color unpaired = new Color(0.95f, 0.2f, 0.2f, 1f);

        var pixels = new Color[N * N];
        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++)
            {
                float cx = (x + 0.5f) / N - 0.5f;
                float cy = (y + 0.5f) / N - 0.5f;
                float u = cx * f.x + cy * f.y;          // along the facing: -0.5 back .. +0.5 front
                float v = cx * l.x + cy * l.y;          // across it
                float av = Mathf.Abs(v);
                float h = role == UndergroundBelt.Role.Entrance ? u : -u; // > 0 = tunnel side

                Color c;
                if (h > 0f)
                {
                    c = hood;
                    if (h < 0.08f) c = rim;
                    else if (av < 0.30f && h < 0.46f) c = opening;
                    if (h > 0.46f || av > 0.46f) c = Mul(hood, 0.7f);
                }
                else
                {
                    c = lane;
                    if (av < 0.38f && Mathf.Repeat((u + av) * 3.2f, 1f) < 0.28f) c = chevron;
                    if (av > 0.46f) c = Mul(lane, 0.6f);
                }

                if (!paired && Mathf.Abs(h - 0.28f) < 0.07f && av < 0.07f) c = unpaired;

                pixels[y * N + x] = c;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N, 0, SpriteMeshType.FullRect);
    }
}