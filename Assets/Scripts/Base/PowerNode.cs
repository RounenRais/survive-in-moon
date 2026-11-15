using System.Collections.Generic;
using UnityEngine;

// A building's place in the base power grid: positive kilowatts = produces (solar panel, generator),
// negative = uses (habitat, greenhouse, garage). BaseSupplies adds them all up.
public class PowerNode : MonoBehaviour
{
    public float kilowatts;

    private static readonly List<PowerNode> all = new List<PowerNode>();
    public static IReadOnlyList<PowerNode> All => all;

    void OnEnable() => all.Add(this);
    void OnDisable() => all.Remove(this);
}
