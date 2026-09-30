using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Import settings for the country astronauts in Assets/Models/Astronauts (made in Blender by
// Tools/build_astronaut_characters.py). An AssetPostprocessor runs by itself every time Unity imports a file:
//  - Generic rig with the hierarchy kept as it is ("Armature Astronaut/root/hüfte/..."), exactly like
//    Astronaut.fbx, so the existing Astronaut.controller clips and AstronautAnimation work on them too
//  - The clips get short names (Idle, Walk, Grab); Idle and Walk loop, Grab plays once
//  - Grab calls InteractionContact when the hands close and InteractionEnd at the end (the same events as the
//    player's pick up), without errors on characters that don't listen to them
//  - Read/Write enabled, so AstronautAnimation can find the soles in the mesh
//  - Removes curves on the file's root object: they would pin the character in place, so a script couldn't move it
// Clip settings are only filled in on the first import: changes made later in the Inspector are kept.
public class AstronautModelPostprocessor : AssetPostprocessor
{
    const string Folder = "Assets/Models/Astronauts/";
    const float GrabContactTime = 0.378f; // Part of the Grab clip where the hands close (frame 18 of 1..46)

    static readonly HashSet<string> LoopingClips = new HashSet<string> { "Idle", "Walk" };

    // Raising this number makes Unity import the models again with the settings below
    public override uint GetVersion() => 2;

    bool IsAstronautModel => assetPath.StartsWith(Folder, StringComparison.Ordinal)
                             && assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

    void OnPreprocessModel()
    {
        if (!IsAstronautModel) return;
        var importer = (ModelImporter)assetImporter;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.importAnimation = true;
        importer.preserveHierarchy = true; // Keep the "Armature Astronaut" object the existing clips point to
        importer.isReadable = true; // AstronautAnimation reads the mesh to find the soles (foot planting)
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
    }

    void OnPreprocessAnimation()
    {
        if (!IsAstronautModel) return;
        var importer = (ModelImporter)assetImporter;
        if (importer.clipAnimations != null && importer.clipAnimations.Length > 0) return; // Already set up

        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        foreach (ModelImporterClipAnimation clip in clips)
        {
            // Blender names the takes like "Armature Astronaut|Walk": keep only the part after the |
            int bar = clip.name.LastIndexOf('|');
            if (bar >= 0) clip.name = clip.name.Substring(bar + 1);

            clip.loopTime = LoopingClips.Contains(clip.name);
            if (clip.name == "Grab")
            {
                clip.events = new[]
                {
                    new AnimationEvent { functionName = "InteractionContact", time = GrabContactTime,
                                         messageOptions = SendMessageOptions.DontRequireReceiver },
                    new AnimationEvent { functionName = "InteractionEnd", time = 0.98f,
                                         messageOptions = SendMessageOptions.DontRequireReceiver }
                };
            }
        }
        importer.clipAnimations = clips;
    }

    void OnPostprocessAnimation(GameObject root, AnimationClip clip)
    {
        if (!IsAstronautModel) return;
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (binding.type == typeof(Transform) && string.IsNullOrEmpty(binding.path))
                AnimationUtility.SetEditorCurve(clip, binding, null);
        }
    }
}
