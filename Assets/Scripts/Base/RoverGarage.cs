using UnityEngine;

// Building the garage is how you get the rover: the rover waits hidden in the scene until a garage is finished,
// then it rolls out next to it, ready to drive.
public class RoverGarage : MonoBehaviour
{
    public Vector3 rolloutOffset = new Vector3(0f, 0f, -38f); // In the garage's own space: in front of the door

    void Start()
    {
        MoonRover rover = FindHiddenRover();
        if (rover == null) return;
        Vector3 spot = BaseArea.GroundPoint(transform.TransformPoint(rolloutOffset)) + Vector3.up * 3f;
        rover.transform.SetPositionAndRotation(spot, transform.rotation);
        rover.gameObject.SetActive(true);
        GameSystems.Notify("The rover is ready - press F next to it to drive");
    }

    static MoonRover FindHiddenRover()
    {
        foreach (MoonRover r in Resources.FindObjectsOfTypeAll<MoonRover>())
            if (r.gameObject.scene.IsValid() && !r.gameObject.activeInHierarchy) return r;
        return null;
    }
}
