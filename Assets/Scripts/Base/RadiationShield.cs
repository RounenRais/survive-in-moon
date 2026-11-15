using UnityEngine;

// Regolith shielding around the habitat: a few metres of packed lunar soil stop most solar-storm particles.
// Without it the base only takes the edge off a storm; with it the crew sits a storm out in safety.
public class RadiationShield : MonoBehaviour
{
    public static int Count { get; private set; }
    public static bool Active => Count > 0;

    void OnEnable()
    {
        Count++;
        ScienceCards.Show("shield");
    }

    void OnDisable() => Count--;
}
