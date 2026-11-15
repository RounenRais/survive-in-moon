using UnityEngine;

// Presentation shortcuts in the game scene:
//   F1 - the next science card (cycles through all of them)
//   F2 - the "Mission lost" screen with the debrief so far
public class DemoKeys : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1)) ScienceCards.ShowNextDemo();
        if (Input.GetKeyDown(KeyCode.F2) && !GameOverUI.IsOpen) GameOverUI.Show(false);
    }
}
