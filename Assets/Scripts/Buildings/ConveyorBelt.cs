using UnityEngine;

/// <summary>
/// The simplest building: carries an item across its own tile, from the edge
/// facing away from its direction to the edge it faces, then hands it to
/// whatever is next. Upgrading a belt makes it move items faster.
/// </summary>
public class ConveyorBelt : FactoryBuilding
{
    // Items enter from the back edge (opposite of facing) instead of the
    // building's center, so they visibly travel the full length of the belt.
    protected override Vector3 EntryLocalOffset =>
        -(Vector3)(Vector2)DirectionUtil.ToVector(Facing) * 0.5f;
}
