using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Portal's section of the machine window: what it needs, how much has arrived.
/// UIManager builds it with Build() and calls Refresh() every frame while the window is open.
/// </summary>
public class PortalInspectorUI
{
    private class Row { public ItemDefinition Item; public int Need; public Text Label; }

    private static readonly Color Accent = new Color(0.95f, 0.62f, 0.12f, 1f);
    private static readonly Color Done = new Color(0.5f, 1f, 0.55f);

    private readonly Portal portal;
    private readonly List<Row> rows = new List<Row>();
    private Text status;
    private RectTransform barFill;
    private Font font;

    private PortalInspectorUI(Portal portal) { this.portal = portal; }

    public static PortalInspectorUI Build(RectTransform parent, Font font, Portal portal)
    {
        var ui = new PortalInspectorUI(portal) { font = font };
        ui.Create(parent);
        return ui;
    }

    private void Create(RectTransform parent)
    {
        Text header = MakeText(parent, "Portal", 18, FontStyle.Bold, 26f);
        header.color = Accent;
        status = MakeText(parent, "", 14, FontStyle.Normal, 26f);
        MakeText(parent, "Deliver these items with belts:", 14, FontStyle.Bold, 24f);

        // progress bar
        var barGo = new GameObject("PortalBar", typeof(RectTransform));
        barGo.transform.SetParent(parent, false);
        var le = barGo.AddComponent<LayoutElement>();
        le.preferredHeight = 16f;
        le.minHeight = 16f;
        var bg = barGo.AddComponent<Image>();
        bg.color = new Color(0.12f, 0.12f, 0.15f, 1f);
        bg.raycastTarget = false;

        var fillGo = new GameObject("Fill", typeof(RectTransform));
        fillGo.transform.SetParent(barGo.transform, false);
        barFill = fillGo.GetComponent<RectTransform>();
        barFill.anchorMin = Vector2.zero;
        barFill.anchorMax = new Vector2(0f, 1f);
        barFill.offsetMin = Vector2.zero;
        barFill.offsetMax = Vector2.zero;
        var fill = fillGo.AddComponent<Image>();
        fill.color = Accent;
        fill.raycastTarget = false;

        foreach (var req in portal.requirements)
        {
            if (req == null || req.item == null) continue;

            var rowGo = new GameObject("Row", typeof(RectTransform));
            rowGo.transform.SetParent(parent, false);
            rowGo.AddComponent<LayoutElement>().preferredHeight = 32f;
            var h = rowGo.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 10f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            h.childControlWidth = true;
            h.childControlHeight = true;

            var sq = new GameObject("Color", typeof(RectTransform));
            sq.transform.SetParent(rowGo.transform, false);
            var sle = sq.AddComponent<LayoutElement>();
            sle.preferredWidth = 26f;
            sle.preferredHeight = 26f;
            var img = sq.AddComponent<Image>();
            img.color = req.item.color;
            img.raycastTarget = false;

            Text label = MakeText(rowGo.transform, "", 15, FontStyle.Normal, 28f);
            label.GetComponent<LayoutElement>().flexibleWidth = 1f;

            rows.Add(new Row { Item = req.item, Need = req.amount, Label = label });
        }

        Refresh();
    }

    public void Refresh()
    {
        if (portal == null) return;

        status.text = portal.IsActivated
            ? "PORTAL ACTIVE"
            : "Charging: " + Mathf.RoundToInt(portal.Progress01 * 100f) + "%";
        status.color = portal.IsActivated ? Done : Color.white;
        if (barFill != null) barFill.anchorMax = new Vector2(portal.Progress01, 1f);

        foreach (var r in rows)
        {
            int have = Mathf.Min(r.Need, portal.GetDelivered(r.Item));
            r.Label.text = r.Item.itemName + "   " + have + " / " + r.Need;
            r.Label.color = have >= r.Need ? Done : Color.white;
        }
    }

    private Text MakeText(Transform parent, string content, int size, FontStyle style, float height)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.AddComponent<LayoutElement>().preferredHeight = height;
        var t = go.AddComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = TextAnchor.MiddleLeft;
        t.color = Color.white;
        t.text = content;
        t.raycastTarget = false;
        return t;
    }
}
