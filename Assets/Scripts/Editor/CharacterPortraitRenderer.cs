using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Editor-only tool. Menu: Survive In Moon > Render Character Portraits
// Takes a "photo" of every character in the CharacterDatabase: head and chest, from the front and turned a
// little, standing in the Idle pose (not the T-pose). Saved as transparent PNG sprites in
// Assets/Characters/Portraits and put into each CharacterDefinition.icon (the crew cards on the build screen
// and a future character select screen show them).
public static class CharacterPortraitRenderer
{
    const string Folder = "Assets/Characters/Portraits";
    const string DatabasePath = "Assets/Resources/CharacterDatabase.asset";

    [MenuItem("Survive In Moon/Render Character Portraits")]
    public static void RenderAll()
    {
        var database = AssetDatabase.LoadAssetAtPath<CharacterDatabase>(DatabasePath);
        if (database == null)
        {
            Debug.LogWarning("Portraits: no CharacterDatabase yet. Run Survive In Moon > Setup Character Switching first.");
            return;
        }
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Characters", "Portraits");

        int count = 0;
        foreach (CharacterDefinition character in database.characters)
        {
            if (character == null || character.prefab == null) continue;
            character.icon = Render(character.prefab, $"{Folder}/{character.id}.png");
            EditorUtility.SetDirty(character);
            count++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"Portraits: {count} character photos in {Folder}.");
    }

    static Sprite Render(GameObject prefab, string path, int size = 512)
    {
        var preview = new PreviewRenderUtility();
        try
        {
            GameObject astronaut = preview.InstantiatePrefabInScene(prefab);
            astronaut.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 200f, 0f)); // Facing the camera, turned a little

            // Stand in the first frame of Idle instead of the bind pose
            Animator animator = astronaut.GetComponentInChildren<Animator>();
            AnimationClip idle = animator != null && animator.runtimeAnimatorController != null
                ? animator.runtimeAnimatorController.animationClips.FirstOrDefault(c => c.name == "Idle")
                : null;
            if (idle != null) idle.SampleAnimation(animator.gameObject, 0.4f);

            Bounds body = MeshBounds(astronaut);
            // Head and chest (with the flag patch): the top ~80% of the body - the helmet is big on these astronauts
            float frameHeight = body.size.y * 0.82f;
            Vector3 target = new Vector3(body.center.x, body.max.y - frameHeight * 0.5f, body.center.z);

            Camera cam = preview.camera;
            cam.fieldOfView = 22f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            float distance = frameHeight * 0.5f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.08f;
            Vector3 viewDirection = new Vector3(0f, 0.12f, -1f).normalized; // Camera in front, a little above
            cam.transform.position = target + viewDirection * distance;
            cam.transform.LookAt(target);
            cam.nearClipPlane = Mathf.Max(0.01f, distance - body.size.magnitude);
            cam.farClipPlane = distance + body.size.magnitude;

            preview.lights[0].intensity = 1.25f;
            preview.lights[0].color = new Color(1f, 0.97f, 0.92f);
            preview.lights[0].transform.rotation = Quaternion.Euler(25f, 25f, 0f);
            preview.lights[1].intensity = 0.8f;
            preview.lights[1].color = new Color(0.6f, 0.75f, 1f);
            preview.lights[1].transform.rotation = Quaternion.Euler(10f, -150f, 0f); // Blue rim light from behind
            preview.ambientColor = new Color(0.4f, 0.41f, 0.45f);

            Texture2D onBlack = ItemIconRenderer.Shoot(preview, size, Color.black);
            Texture2D onWhite = ItemIconRenderer.Shoot(preview, size, Color.white);
            Texture2D photo = ItemIconRenderer.Combine(onBlack, onWhite);
            Object.DestroyImmediate(onBlack);
            Object.DestroyImmediate(onWhite);
            File.WriteAllBytes(path, photo.EncodeToPNG());
            Object.DestroyImmediate(photo);
        }
        finally
        {
            preview.Cleanup();
        }
        return ItemIconRenderer.ImportSprite(path);
    }

    // Bounds of the posed mesh (Renderer.bounds of an animated model is padded)
    static Bounds MeshBounds(GameObject go)
    {
        bool found = false;
        Bounds bounds = new Bounds(go.transform.position, Vector3.zero);
        foreach (SkinnedMeshRenderer smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh = new Mesh();
            smr.BakeMesh(mesh, false);
            Matrix4x4 toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            foreach (Vector3 v in mesh.vertices)
            {
                Vector3 w = toWorld.MultiplyPoint3x4(v);
                if (!found) { bounds = new Bounds(w, Vector3.zero); found = true; }
                else bounds.Encapsulate(w);
            }
            Object.DestroyImmediate(mesh);
        }
        return bounds;
    }
}
