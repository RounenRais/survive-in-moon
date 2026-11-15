using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Editor-only helper so an outside tool (Claude Code) can drive the open editor without clicking:
// it watches Temp/ClaudeBridge/cmd.txt, runs the command written there, and writes the result to
// Temp/ClaudeBridge/out.txt (Temp is never imported or committed). Commands, one per file:
//   diag                     -> animator / controller diagnostics (edit mode and play mode)
//   reimport-controllers     -> force-reloads the astronaut Animator Controllers from disk
//   render                   -> transparent PNG photos of astronauts, rover and buildings into Exports/Renders
//   play / stop              -> enter / leave play mode
//   menu <Menu/Path>         -> runs any editor menu item
[InitializeOnLoad]
public static class ClaudeBridge
{
    static readonly string Dir = Path.GetFullPath("Temp/ClaudeBridge");
    static readonly string CmdPath = Path.Combine(Dir, "cmd.txt");
    static readonly string OutPath = Path.Combine(Dir, "out.txt");
    static double nextCheck;

    static ClaudeBridge()
    {
        Directory.CreateDirectory(Dir);
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 0.5;
        if (!File.Exists(CmdPath)) return;

        string cmd = File.ReadAllText(CmdPath).Trim();
        File.Delete(CmdPath);
        string result;
        try { result = Run(cmd); }
        catch (System.Exception e) { result = "ERROR: " + e; }
        File.WriteAllText(OutPath, $"[{System.DateTime.Now:HH:mm:ss}] {cmd}\n{result}\n");
    }

    static string Run(string cmd)
    {
        if (cmd == "diag") return Diagnose();
        if (cmd == "reimport-controllers") return ReimportControllers();
        if (cmd == "render") return BridgeRenderer.RenderAll("Exports/Renders");
        if (cmd == "intro-images") return IntroImages.Render();
        if (cmd == "play") { EditorApplication.isPlaying = true; return "entering play mode"; }
        if (cmd == "stop") { EditorApplication.isPlaying = false; return "leaving play mode"; }
        if (cmd.StartsWith("menu ")) return EditorApplication.ExecuteMenuItem(cmd.Substring(5)) ? "ok" : "menu item not found";
        if (cmd.StartsWith("import "))
        {
            AssetDatabase.ImportAsset(cmd.Substring(7), ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var o = AssetDatabase.LoadMainAssetAtPath(cmd.Substring(7));
            return o != null ? $"reimported {o.name} dirty={EditorUtility.IsDirty(o)}" : "not found";
        }
        if (cmd.StartsWith("matf ")) // matf <material path> <property> <value>
        {
            string[] p = cmd.Split(' ');
            var m = AssetDatabase.LoadAssetAtPath<Material>(p[1]);
            if (m == null) return "material not found";
            m.SetFloat(p[2], float.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture));
            EditorUtility.SetDirty(m);
            return $"{p[2]}={m.GetFloat(p[2])}";
        }
        if (cmd.StartsWith("feature ")) // feature <name> on|off   (on the project's URP renderers)
        {
            string[] p = cmd.Split(new[] { ' ' }, 3);
            string name = p[1].Replace('_', ' ');
            var sb = new StringBuilder();
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets" }))
            {
                var data = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                foreach (var f in data.rendererFeatures)
                    if (f != null && f.name == name) { f.SetActive(p[2] == "on"); EditorUtility.SetDirty(f); sb.AppendLine($"{data.name}/{name} -> {p[2]}"); }
            }
            return sb.Length > 0 ? sb.ToString() : "feature not found";
        }
        if (cmd.StartsWith("ui ")) // ui inventory|build|crew|close   (play mode: opens a window for a screenshot)
        {
            string which = cmd.Substring(3);
            var inventory = Object.FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
            if (inventory != null) inventory.SetOpen(which == "inventory");
            if (which == "build") BuildMenuUI.Open(); else BuildMenuUI.Close();
            if (which == "crew") CrewUI.Open(); else CrewUI.Close();
            var player = Object.FindFirstObjectByType<InteractionSystem>();
            if (which == "craft" && player != null) CraftingUI.Open(player.Inventory); else CraftingUI.Close();
            MapUI.Open(which == "map");
            return $"ui {which}: inventory={(inventory != null ? inventory.IsOpen + " in " + inventory.gameObject.scene.name + " activeInHierarchy=" + inventory.gameObject.activeInHierarchy : "none")} build={BuildMenuUI.IsOpen} crew={CrewUI.IsOpen}";
        }
        if (cmd == "gizmos off" || cmd == "gizmos on") // The Game view's Gizmos toggle (editor-only lines over the game)
        {
            System.Type type = System.Type.GetType("UnityEditor.GameView,UnityEditor");
            var field = type?.GetField("m_Gizmos", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field == null) return "Game view field not found";
            int count = 0;
            foreach (Object view in Resources.FindObjectsOfTypeAll(type)) { field.SetValue(view, cmd == "gizmos on"); count++; }
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            return $"{cmd} on {count} game view(s)";
        }
        if (cmd.StartsWith("click ")) // Presses the button whose label is this text (play mode)
        {
            string label = cmd.Substring(6);
            foreach (var b in Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None))
            {
                var t = b.GetComponentInChildren<TMPro.TMP_Text>();
                if (t != null && t.text == label) { b.onClick.Invoke(); return "clicked " + label; }
            }
            return "no button " + label;
        }
        if (cmd.StartsWith("give ")) // give <item id> <amount>
        {
            string[] p = cmd.Split(' ');
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"Assets/Items/{p[1]}.asset");
            var inv = Object.FindFirstObjectByType<InteractionSystem>()?.Inventory;
            if (item == null || inv == null) return "no item or inventory";
            int left = inv.TryAddItem(item, p.Length > 2 ? int.Parse(p[2]) : 1);
            return $"gave {p[1]} (not added {left})";
        }
        if (cmd.StartsWith("tp ")) // tp ship | tp base | tp <x> <z>
        {
            var pm = Object.FindFirstObjectByType<PlayerMovement>();
            string[] p = cmd.Split(' ');
            Vector3 to = p[1] == "ship" && ShipWreck.Instance != null ? ShipWreck.Instance.transform.position + new Vector3(0f, 0f, -45f)
                       : p[1] == "base" ? BaseArea.Main.transform.position + new Vector3(30f, 0f, 30f)
                       : new Vector3(float.Parse(p[1]), 0f, float.Parse(p[2]));
            to = BaseArea.GroundPoint(to) + Vector3.up * 3f;
            var cc = pm.GetComponent<CharacterController>();
            cc.enabled = false; pm.transform.position = to; cc.enabled = true;
            return "teleported to " + to;
        }
        if (cmd.StartsWith("near")) // Root objects (and their children) with renderers near the player / base
        {
            Vector3 c = BaseArea.Main != null ? BaseArea.Main.transform.position : Vector3.zero;
            float r = cmd.Length > 5 ? float.Parse(cmd.Substring(5)) : 400f;
            var sb2 = new StringBuilder();
            foreach (GameObject go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                {
                    if (t.parent != null && t.parent.parent != null) continue; // Roots and their direct children
                    Renderer rend = t.GetComponentInChildren<Renderer>(true);
                    if (rend == null) continue;
                    float d = Vector3.Distance(rend.bounds.center, c);
                    if (d < r) sb2.AppendLine($"{GetPathB(t)} d={d:0} size={rend.bounds.size} active={t.gameObject.activeInHierarchy}");
                }
            return sb2.ToString();
        }
        if (cmd == "crew")
        {
            var sb3 = new StringBuilder();
            foreach (CrewMember m in Object.FindObjectsByType<CrewMember>(FindObjectsSortMode.None))
            {
                Transform hips = null;
                foreach (Transform t in m.GetComponentsInChildren<Transform>()) if (t.name == "hüfte") { hips = t; break; }
                var anim = m.GetComponentInChildren<Animator>();
                string clip = anim != null && anim.runtimeAnimatorController != null && anim.GetCurrentAnimatorClipInfo(0).Length > 0 ? anim.GetCurrentAnimatorClipInfo(0)[0].clip.name : "-";
                sb3.AppendLine($"{m.name} player={m.isPlayer} order={m.Order} pos={m.transform.position} yaw={m.transform.eulerAngles.y:0.0} hips={(hips != null ? hips.position.ToString() : "-")} clip={clip}");
            }
            return sb3.ToString();
        }
        if (cmd == "paused") return $"paused={EditorApplication.isPaused} timeScale={Time.timeScale}";
        if (cmd == "unpause") { EditorApplication.isPaused = false; return "unpaused"; }
        if (cmd.StartsWith("crewmove ")) // crewmove <x> <z>: every crew mate walks there
        {
            string[] p = cmd.Split(' ');
            var to = BaseArea.GroundPoint(new Vector3(float.Parse(p[1]), 0f, float.Parse(p[2])));
            int n = 0;
            foreach (CrewMember m in Object.FindObjectsByType<CrewMember>(FindObjectsSortMode.None))
                if (!m.isPlayer) { m.MoveTo(to); n++; }
            return $"{n} crew walking to {to}";
        }
        if (cmd.StartsWith("record ")) // record <shot> <seconds> <folder>   (play mode)
        {
            string[] p = cmd.Split(new[] { ' ' }, 4);
            CinematicRecorder.Begin(p[1], float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture), p[3]);
            return "recording " + p[1];
        }
        if (cmd == "recstatus") return $"busy={CinematicRecorder.Busy} frames={CinematicRecorder.FramesWritten}";
        if (cmd.StartsWith("hud ")) // hud on|off: hide the screen-space UI for clean screenshots
        {
            foreach (Canvas cv in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (cv.renderMode == RenderMode.ScreenSpaceOverlay) cv.enabled = cmd == "hud on";
            return cmd;
        }
        if (cmd.StartsWith("shotx ")) // shotx <path>: 2x supersampled game view screenshot
        {
            ScreenCapture.CaptureScreenshot(System.IO.Path.GetFullPath(cmd.Substring(6)), 2);
            return "capturing";
        }
        if (cmd == "cards")
        {
            var go = GameObject.Find("Science Cards");
            var cv = GameObject.Find("Science Cards") != null ? Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None) : null;
            string canv = "";
            if (cv != null) foreach (var c in cv) if (c.name == "Science Cards") canv += $" canvas active={c.gameObject.activeInHierarchy} children={c.transform.childCount} cardActive={(c.transform.childCount > 0 ? c.transform.GetChild(0).gameObject.activeSelf.ToString() : "-")}";
            var st = SurvivalStats.Player;
            return $"obj={(go != null)}{canv} inBase={(st != null ? st.InLifeSupport.ToString() : "-")} dose={MissionLog.DoseMsv:0.00}";
        }
        if (cmd == "demo card") { ScienceCards.ShowNextDemo(); return "card"; }
        if (cmd == "demo fail") { GameOverUI.Show(false); return "fail"; }
        if (cmd.StartsWith("buildwin ")) // buildwin <exe path>: Windows 64-bit player with the build-settings scenes
        {
            string exe = cmd.Substring(9);
            PlayerSettings.productName = "Survive in Moon";
            PlayerSettings.companyName = "TAY Team";
            PlayerSettings.bundleVersion = "1.0";
            var scenes = System.Array.FindAll(EditorBuildSettings.scenes, sc => sc.enabled);
            var options = new BuildPlayerOptions
            {
                scenes = System.Array.ConvertAll(scenes, sc => sc.path),
                locationPathName = exe,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(options);
            var sum = report.summary;
            return $"result={sum.result} size={sum.totalSize / (1024 * 1024)} MB time={sum.totalTime} errors={sum.totalErrors} scenes={string.Join(",", options.scenes)}";
        }
        if (cmd.StartsWith("renderone ")) // renderone <prefab asset path>|<png path>
        {
            string[] p = cmd.Substring(10).Split('|');
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p[0]);
            if (prefab == null) return "no prefab " + p[0];
            BridgeRenderer.Render(prefab, p[1], 2048, p.Length > 2 ? float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture) : float.NaN);
            return "rendered " + p[1];
        }
        if (cmd == "kill") { SurvivalStats.Player?.Hurt(1000f); return "hurt"; }
        if (cmd == "stats")
        {
            var s = SurvivalStats.Player;
            if (s == null) return "no stats";
            var sup = BaseSupplies.Instance;
            return $"O2 {s.Oxygen:0}/{s.OxygenCapacity:0} food {s.Food:0} water {s.Water:0} warmth {s.Warmth:0} stamina {s.Stamina:0} rad {s.Radiation:0.0} hp {s.Health:0} " +
                   $"inBase {s.InLifeSupport} dist {s.DistanceFromBase:0} shade {s.InShadow} | storm {SolarStorms.Strength:0.00} {SolarStorms.Current?.classType} warn {SolarStorms.Warning} in {SolarStorms.SecondsToStorm:0} flares {SolarStorms.Flares.Count}" +
                   (sup != null ? $" | power {sup.PowerProduced}/{sup.PowerUsed} crew {sup.CrewCount} food {sup.Food:0.0}" : "");
        }
        if (cmd == "save") return UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes() ? "saved" : "save failed";
        if (cmd.StartsWith("open ")) return UnityEditor.SceneManagement.EditorSceneManager.OpenScene(cmd.Substring(5)).path;
        if (cmd.StartsWith("shot ")) // Game view screenshot (play mode), written at the end of the frame
        {
            ScreenCapture.CaptureScreenshot(Path.GetFullPath(cmd.Substring(5)));
            return "capturing " + Path.GetFullPath(cmd.Substring(5));
        }
        if (cmd == "scene") return UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path +
                                   " dirty=" + UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty;
        if (cmd == "errors") return LastErrors();
        return "unknown command";
    }

    // Errors and warnings logged since the editor started (kept by the bridge), newest last
    static readonly System.Collections.Generic.List<string> log = new System.Collections.Generic.List<string>();
    [InitializeOnLoadMethod]
    static void HookLog() => Application.logMessageReceived += (message, stack, type) =>
    {
        if (type == LogType.Log) return;
        log.Add($"{type}: {message}");
        if (log.Count > 60) log.RemoveAt(0);
    };
    static string LastErrors() => log.Count == 0 ? "none" : string.Join("\n", log);

    static string ReimportControllers()
    {
        var sb = new StringBuilder();
        foreach (string guid in AssetDatabase.FindAssets("t:AnimatorController", new[] { "Assets/Prefabs/Astronauts/Animators" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            sb.AppendLine("reimported " + path);
        }
        // The prefabs point at the controllers: reload them too so they pick up the fixed assets
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Astronauts", "Assets/Prefabs/NPCs" }))
            AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        Resources.UnloadUnusedAssets();
        return sb.ToString() + Diagnose();
    }

    static string Diagnose()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"playing={EditorApplication.isPlaying}");
        sb.AppendLine("--- controller assets ---");
        foreach (string guid in AssetDatabase.FindAssets("t:AnimatorController", new[] { "Assets/Prefabs/Astronauts/Animators" }))
        {
            var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GUIDToAssetPath(guid));
            sb.AppendLine(Describe(c));
        }

        sb.AppendLine("--- astronaut prefabs ---");
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Astronauts" }))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            Animator a = prefab != null ? prefab.GetComponentInChildren<Animator>(true) : null;
            sb.AppendLine($"{(prefab != null ? prefab.name : "?")}: animator={(a != null)} controller={(a != null && a.runtimeAnimatorController != null ? a.runtimeAnimatorController.name : "null")}");
        }
        foreach (string guid in AssetDatabase.FindAssets("t:CharacterDefinition"))
        {
            var cd = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            Animator a = cd != null && cd.prefab != null ? cd.prefab.GetComponentInChildren<Animator>(true) : null;
            sb.AppendLine($"Character {cd?.id}: prefab={(cd?.prefab != null ? cd.prefab.name : "null")} controller={(a != null && a.runtimeAnimatorController != null ? a.runtimeAnimatorController.name : "null")}");
        }

        sb.AppendLine("--- player ---");
        foreach (PlayerMovement pm in Object.FindObjectsByType<PlayerMovement>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            sb.AppendLine($"PlayerMovement on '{pm.name}' active={pm.isActiveAndEnabled}");
            foreach (Animator a in pm.GetComponentsInChildren<Animator>(true))
                sb.AppendLine("  " + DescribeAnimator(a, a == pm.animator));
            if (pm.animator != null && !pm.animator.transform.IsChildOf(pm.transform))
                sb.AppendLine("  pm.animator is NOT a child: " + DescribeAnimator(pm.animator, true));
            if (pm.animator == null) sb.AppendLine("  pm.animator = null");
            var pc = pm.GetComponent<PlayerCharacter>();
            if (pc != null) sb.AppendLine($"  PlayerCharacter current={(pc.Current != null ? pc.Current.id : "null")} model={(pc.model != null ? pc.model.name : "null")}");
        }
        return sb.ToString();
    }

    static string Describe(RuntimeAnimatorController rc)
    {
        if (rc == null) return "controller=null";
        var c = rc as AnimatorController;
        if (c == null) return $"{rc.name} (override/other) clips={rc.animationClips.Length}";
        var states = c.layers.Length > 0 ? c.layers[0].stateMachine.states : new ChildAnimatorState[0];
        return $"{c.name}: layers={c.layers.Length} params=[{string.Join(",", c.parameters.Select(p => p.name))}] " +
               $"states=[{string.Join(",", states.Select(s => s.state.name + ":" + (s.state.motion != null ? s.state.motion.name : "none")))}] clips={c.animationClips.Length}";
    }

    static string DescribeAnimator(Animator a, bool isPlayers)
    {
        string s = $"Animator '{GetPath(a.transform)}' used={isPlayers} goActive={a.gameObject.activeInHierarchy} enabled={a.enabled} " +
                   $"avatar={(a.avatar != null ? a.avatar.name : "null")} rootMotion={a.applyRootMotion} speed={a.speed} " +
                   $"controller=[{Describe(a.runtimeAnimatorController)}]";
        if (Application.isPlaying && a.isActiveAndEnabled && a.runtimeAnimatorController != null)
        {
            s += $" initialized={a.isInitialized} paramCount={a.parameterCount}";
            if (a.parameterCount > 0 && a.parameters.Any(p => p.name == "Speed")) s += $" Speed={a.GetFloat("Speed"):0.00}";
            var clips = a.GetCurrentAnimatorClipInfo(0);
            s += $" playing=[{string.Join(",", clips.Select(ci => ci.clip.name + "@" + ci.weight.ToString("0.00")))}]";
        }
        return s;
    }

    static string GetPathB(Transform t) => t.parent == null ? t.name : t.parent.name + "/" + t.name;
    static string GetPath(Transform t) => t.parent == null ? t.name : GetPath(t.parent) + "/" + t.name;
}

// Full-size transparent "product photos" of the game's objects under a clean studio light
public static class BridgeRenderer
{
    public static string RenderAll(string folder)
    {
        Directory.CreateDirectory(folder);
        var sb = new StringBuilder();
        string[] sources = { "Assets/Prefabs/Astronauts", "Assets/Prefabs/Vehicles", "Assets/Prefabs/Buildings" };
        foreach (string source in sources)
        {
            string sub = Path.Combine(folder, Path.GetFileName(source));
            Directory.CreateDirectory(sub);
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { source }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetDirectoryName(path).Replace('\\', '/') != source) continue; // skip sub folders
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                string file = Path.Combine(sub, prefab.name + ".png");
                Render(prefab, file, 2048); // Big, so it's still sharp after cropping to the object
                sb.AppendLine(Path.GetFullPath(file));
            }
        }
        return sb.ToString();
    }

    public static void Render(GameObject prefab, string file, int size, float yawOverride = float.NaN)
    {
        var preview = new PreviewRenderUtility();
        try
        {
            GameObject go = preview.InstantiatePrefabInScene(prefab);
            bool astronaut = go.GetComponentInChildren<SkinnedMeshRenderer>() != null;
            // Astronauts face the camera (turned a little); objects get a 3/4 view
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, float.IsNaN(yawOverride) ? (astronaut ? 200f : 215f) : yawOverride, 0f));

            Animator animator = go.GetComponentInChildren<Animator>();
            AnimationClip idle = animator != null && animator.runtimeAnimatorController != null
                ? animator.runtimeAnimatorController.animationClips.FirstOrDefault(c => c.name == "Idle") : null;
            if (idle != null) idle.SampleAnimation(animator.gameObject, 0.4f);

            Bounds b = Bounds(go);
            Camera cam = preview.camera;
            cam.fieldOfView = 25f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            Vector3 viewDirection = new Vector3(0f, astronaut ? 0.12f : 0.45f, -1f).normalized;
            float radius = b.extents.magnitude;
            float distance = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.02f;
            cam.transform.position = b.center + viewDirection * distance;
            cam.transform.LookAt(b.center);
            cam.nearClipPlane = Mathf.Max(0.01f, distance - radius * 2f);
            cam.farClipPlane = distance + radius * 2f;

            // Studio light: warm key from front-left-top, cool rim from behind, soft ambient fill
            preview.lights[0].intensity = 1.3f;
            preview.lights[0].color = new Color(1f, 0.97f, 0.92f);
            preview.lights[0].transform.rotation = Quaternion.Euler(35f, 30f, 0f);
            preview.lights[1].intensity = 0.8f;
            preview.lights[1].color = new Color(0.6f, 0.75f, 1f);
            preview.lights[1].transform.rotation = Quaternion.Euler(15f, -150f, 0f);
            preview.ambientColor = new Color(0.42f, 0.43f, 0.47f);

            Texture2D onBlack = ItemIconRenderer.Shoot(preview, size, Color.black);
            Texture2D onWhite = ItemIconRenderer.Shoot(preview, size, Color.white);
            Texture2D combined = ItemIconRenderer.Combine(onBlack, onWhite);
            Texture2D photo = CropToContent(combined, 0.06f);
            if (photo != combined) Object.DestroyImmediate(combined);
            File.WriteAllBytes(file, photo.EncodeToPNG());
            Object.DestroyImmediate(onBlack);
            Object.DestroyImmediate(onWhite);
            Object.DestroyImmediate(photo);
        }
        finally
        {
            preview.Cleanup();
        }
    }

    // Cuts the empty transparent border away and keeps a square with a little padding around the object
    static Texture2D CropToContent(Texture2D src, float padding)
    {
        int w = src.width, h = src.height;
        Color32[] px = src.GetPixels32();
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[y * w + x].a > 8)
                {
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
        if (maxX < 0) return src;

        int side = Mathf.Max(maxX - minX + 1, maxY - minY + 1);
        side = Mathf.RoundToInt(side * (1f + padding * 2f));
        int cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
        var dst = new Texture2D(side, side, TextureFormat.RGBA32, false);
        var outPx = new Color32[side * side];
        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                int sx = cx - side / 2 + x, sy = cy - side / 2 + y;
                outPx[y * side + x] = sx >= 0 && sx < w && sy >= 0 && sy < h ? px[sy * w + sx] : new Color32(0, 0, 0, 0);
            }
        dst.SetPixels32(outPx);
        dst.Apply();
        return dst;
    }

    static Bounds Bounds(GameObject go)
    {
        bool found = false;
        var bounds = new Bounds(go.transform.position, Vector3.zero);
        foreach (SkinnedMeshRenderer smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh = new Mesh();
            smr.BakeMesh(mesh, false);
            Matrix4x4 toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            foreach (Vector3 v in mesh.vertices)
            {
                Vector3 w = toWorld.MultiplyPoint3x4(v);
                if (!found) { bounds = new Bounds(w, Vector3.zero); found = true; } else bounds.Encapsulate(w);
            }
            Object.DestroyImmediate(mesh);
        }
        foreach (MeshRenderer r in go.GetComponentsInChildren<MeshRenderer>())
        {
            if (!found) { bounds = r.bounds; found = true; } else bounds.Encapsulate(r.bounds);
        }
        return bounds;
    }
}
