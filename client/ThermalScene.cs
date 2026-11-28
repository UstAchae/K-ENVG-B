using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace EnvgFusion;

// What the thermal lens camera must see differently from the eye (the user's, 2026-11-26: "我只要挡住玻璃，不画墙壁上的涂鸦和
// 字"): long-wave infrared does not pass window glass, and paint on a wall is the wall's own temperature. The game's own
// thermal (the T-7's) gets both from its switch (_ThermalVisionOn), which has every surface draw its material's
// preset "temperature" instead of its colour, and so flattens everything else too; here the colour stays (the picture
// as before) and only these two are changed:
// - decals (the renderers whose every material is drawn by a decal shader: graffiti, signs, stains) are left out of the
//   lens camera's render (hidden for it alone, shown again right after);
// - window glass (materials whose shader the T-7 shows solid, see SolidForThermal; the WindowsManager switches
//   single-material windows off and draws them itself, for the main and optic cameras only) is drawn by
//   its mesh into the lens camera's diffuse colour as an even surface, against its depth, before the colour is copied.
//   (Chosen by any see-through material with a thermal temperature, 0.1.22 took hundreds of puddles and stains as
//   glass, a grey over the picture, and missed the windows, whose shader has none.)
// Which renderers those are is found once per level scene as it loads (not while playing) and kept.
internal static class ThermalScene
{
    internal sealed class Part
    {
        public Renderer Renderer = null!;
        public int Sub;
        public Mesh Mesh = null!;
        public EFT.Interactive.WindowBreaker? Breaker;
        // a pane that does not move, its triangles read once as its scene loaded (in the world): drawn as part of its
        // block's one mesh, not on its own
        public Vector3[]? Triangles;
        public long Block;
        public bool InBlock;
        public Bounds Where;  // where the whole pane stood as its scene loaded
        public Bounds Near;   // the same with room to move (a door's swing), for the ones drawn on their own
        public bool Free;     // on a body of its own that physics moves: it can end up anywhere
        public bool Broken;   // broken, and its stuck pieces could not be merged: they are drawn one by one
    }
    internal struct Decal
    {
        public Renderer Renderer;
        public Bounds Bounds;   // as its level scene loaded (the levels' decals do not move)
    }
    internal static readonly List<Decal> Decals = new List<Decal>();
    internal static readonly List<Part> Glass = new List<Part>();
    internal static System.Action<string>? Note;
    private static readonly List<Renderer> _hidden = new List<Renderer>();
    private static readonly List<Renderer> _found = new List<Renderer>();
    private static readonly List<Material> _materials = new List<Material>();
    private static bool _hooked, _gone;
    private static readonly System.Predicate<Decal> DecalGone = d => d.Renderer == null;
    private static readonly System.Predicate<Part> GlassGone = p => p.Renderer == null;

    // ---- the panes that do not move, merged ----
    // Drawn one by one, a town's windows cost the main thread 6.3 ms a frame (Ground Zero, 7696 panes known, 3379 in
    // the lens's view; measured in the user's raid 2026-11-28: the ECOTI's frame 28 ms, 19.5 with no glass drawn).
    // The panes of a level stand still, so their triangles are read once as each scene loads and merged into one mesh
    // per block of the map (BlockSize a side); a frame then tests and draws the blocks, a few dozen. A pane the game
    // breaks (WindowBreaker.Break) leaves its block, which is made again without it, and its stuck pieces are drawn
    // as before. What moves (a door's glass) or could not be read stays drawn on its own.
    private const float BlockSize = 32f;
    private sealed class Block
    {
        public readonly List<Part> Parts = new List<Part>();
        public Mesh? Mesh;
        public Bounds Bounds;
        public bool Dirty;
    }
    private static readonly Dictionary<long, Block> _blocks = new Dictionary<long, Block>();
    private static readonly List<Block> _blockList = new List<Block>();
    private static readonly List<Part> _single = new List<Part>();    // drawn on their own (moving, or not read)
    private static readonly List<Part> _broken = new List<Part>();    // broken windows: their stuck pieces
    private static readonly Dictionary<EFT.Interactive.WindowBreaker, Part> _byBreaker = new Dictionary<EFT.Interactive.WindowBreaker, Part>();
    private static bool _blocksDirty;
    private static float _buildAfter;
    private static double _readMs;
    private static int _merged, _notRead, _moving, _whyLogs, _wasBroken;

    private static long BlockOf(Vector3 at) => ((long)Mathf.FloorToInt(at.x / BlockSize) * 1000003L) + Mathf.FloorToInt(at.z / BlockSize);

    // a mesh's vertices and indices, read once per scene's collection (a readable mesh from the mesh itself, the
    // others, most of the game's, back from the graphics buffers)
    private sealed class MeshData
    {
        public Vector3[]? Vertices;
        public int[]? Indices;   // the whole index buffer (read back), or null where the mesh gives them per sub-mesh
        public bool Ok;
        public string Why = "";
    }
    private static readonly Dictionary<Mesh, MeshData> _meshData = new Dictionary<Mesh, MeshData>();

    private static MeshData Read(Mesh mesh)
    {
        if (_meshData.TryGetValue(mesh, out MeshData? known))
            return known;
        var data = new MeshData();
        _meshData[mesh] = data;
        try
        {
            if (mesh.isReadable)
            {
                data.Vertices = mesh.vertices;
                data.Ok = data.Vertices != null && data.Vertices.Length > 0;
                data.Why = "readable, " + (data.Vertices?.Length ?? 0) + " vertices";
                return data;
            }
            int stream = mesh.GetVertexAttributeStream(VertexAttribute.Position);
            int offset = mesh.GetVertexAttributeOffset(VertexAttribute.Position);
            VertexAttributeFormat format = mesh.GetVertexAttributeFormat(VertexAttribute.Position);
            int dimension = mesh.GetVertexAttributeDimension(VertexAttribute.Position);
            data.Why = "position stream " + stream + " offset " + offset + " " + format + " x" + dimension;
            if (stream < 0 || dimension < 3 || (format != VertexAttributeFormat.Float32 && format != VertexAttributeFormat.Float16))
                return data;
            using (GraphicsBuffer vb = mesh.GetVertexBuffer(stream))
            {
                if (vb == null)
                    return data;
                int stride = vb.stride, count = vb.count;
                var bytes = new byte[stride * count];
                vb.GetData(bytes);
                var vertices = new Vector3[count];
                if (format == VertexAttributeFormat.Float32)
                    for (int i = 0, at = offset; i < count; i++, at += stride)
                        vertices[i] = new Vector3(System.BitConverter.ToSingle(bytes, at), System.BitConverter.ToSingle(bytes, at + 4), System.BitConverter.ToSingle(bytes, at + 8));
                else
                    for (int i = 0, at = offset; i < count; i++, at += stride)
                        vertices[i] = new Vector3(Mathf.HalfToFloat(System.BitConverter.ToUInt16(bytes, at)), Mathf.HalfToFloat(System.BitConverter.ToUInt16(bytes, at + 2)), Mathf.HalfToFloat(System.BitConverter.ToUInt16(bytes, at + 4)));
                data.Vertices = vertices;
            }
            using (GraphicsBuffer ib = mesh.GetIndexBuffer())
            {
                if (ib == null)
                    return data;
                int n = ib.count;
                var indices = new int[n];
                if (ib.stride == 2)
                {
                    var small = new ushort[n];
                    ib.GetData(small);
                    for (int i = 0; i < n; i++)
                        indices[i] = small[i];
                }
                else
                    ib.GetData(indices);
                data.Indices = indices;
            }
            data.Ok = true;
        }
        catch (System.Exception e)
        {
            data.Ok = false;
            data.Why += "; threw " + e.GetType().Name + ": " + e.Message;
        }
        return data;
    }

    private static bool Still(Transform t)
    {
        return t.GetComponentInParent<EFT.Interactive.WorldInteractiveObject>(true) == null && t.GetComponentInParent<Rigidbody>(true) == null
            && t.GetComponentInParent<Animator>(true) == null && t.GetComponentInParent<Animation>(true) == null;
    }

    // one pane's triangles in the world, or null where they cannot be had or do not lie where its renderer does
    private static string _why = "";
    private static Vector3[]? TrianglesOf(Mesh mesh, int sub, Matrix4x4 toWorld, Bounds where)
    {
        MeshData data = Read(mesh);
        _why = "mesh not read (" + data.Why + ")";
        if (!data.Ok || data.Vertices == null)
            return null;
        SubMeshDescriptor d = mesh.GetSubMesh(sub);
        _why = "sub-mesh " + d.topology + " " + d.indexCount + " indices";
        if (d.topology != MeshTopology.Triangles || d.indexCount < 3)
            return null;
        _why = "indices out of range (" + data.Why + ")";
        Vector3[] vertices = data.Vertices;
        var triangles = new Vector3[d.indexCount - d.indexCount % 3];
        int[] indices;
        int first, add;
        if (data.Indices != null)
        {
            indices = data.Indices;
            first = d.indexStart;
            add = d.baseVertex;
            if (first + triangles.Length > indices.Length)
                return null;
        }
        else
        {
            indices = mesh.GetIndices(sub, true);
            first = 0;
            add = 0;
            if (indices.Length < triangles.Length)
                return null;
        }
        Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
        for (int i = 0; i < triangles.Length; i++)
        {
            int v = indices[first + i] + add;
            if (v < 0 || v >= vertices.Length)
                return null;
            Vector3 p = toWorld.MultiplyPoint3x4(vertices[v]);
            triangles[i] = p;
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        // (read right: it lies where the renderer says it is, to 10 cm)
        const float slack = 0.1f;
        Vector3 lo = where.min, hi = where.max;
        if (min.x < lo.x - slack || min.y < lo.y - slack || min.z < lo.z - slack || max.x > hi.x + slack || max.y > hi.y + slack || max.z > hi.z + slack)
        {
            _why = "read " + min.ToString("F2") + ".." + max.ToString("F2") + " but the renderer is at " + lo.ToString("F2") + ".." + hi.ToString("F2") + " (" + data.Why + ")";
            return null;
        }
        return triangles;
    }

    private static void IntoBlock(Part part, Bounds where)
    {
        part.Block = BlockOf(where.center);
        if (!_blocks.TryGetValue(part.Block, out Block? block))
        {
            block = new Block();
            _blocks[part.Block] = block;
            _blockList.Add(block);
        }
        block.Parts.Add(part);
        block.Dirty = true;
        _blocksDirty = true;
    }

    private static readonly List<Vector3> _buildVertices = new List<Vector3>(), _buildNormals = new List<Vector3>();
    private static readonly List<int> _buildIndices = new List<int>();
    private static void Build(Block block)
    {
        block.Dirty = false;
        _buildVertices.Clear();
        _buildNormals.Clear();
        _buildIndices.Clear();
        for (int k = block.Parts.Count - 1; k >= 0; k--)
        {
            Part part = block.Parts[k];
            if (part.Renderer == null)
            {
                block.Parts.RemoveAt(k);
                continue;
            }
            if (part.Broken || part.Triangles == null)
                continue;
            Vector3[] t = part.Triangles;
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                Vector3 normal = Vector3.Cross(t[i + 1] - t[i], t[i + 2] - t[i]);
                float length = normal.magnitude;
                if (length < 1e-12f)
                    continue;
                normal /= length;
                for (int c = 0; c < 3; c++)
                {
                    _buildIndices.Add(_buildVertices.Count);
                    _buildVertices.Add(t[i + c]);
                    _buildNormals.Add(normal);
                }
            }
        }
        if (_buildVertices.Count == 0)
        {
            if (block.Mesh != null)
                Object.Destroy(block.Mesh);
            block.Mesh = null;
            return;
        }
        if (block.Mesh == null)
            block.Mesh = new Mesh { name = "Justachae thermal window glass, merged", indexFormat = IndexFormat.UInt32 };
        else
            block.Mesh.Clear();
        block.Mesh.SetVertices(_buildVertices);
        block.Mesh.SetNormals(_buildNormals);
        block.Mesh.SetTriangles(_buildIndices, 0, true);
        block.Bounds = block.Mesh.bounds;
        block.Mesh.UploadMeshData(false);
    }

    // the blocks changed are made again: a moment after the last scene came in (while the level still loads), or at
    // once when one is needed for a frame
    private static void BuildDirty()
    {
        if (!_blocksDirty)
            return;
        _blocksDirty = false;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        int built = 0;
        for (int k = _blockList.Count - 1; k >= 0; k--)
        {
            Block block = _blockList[k];
            if (!block.Dirty)
                continue;
            Build(block);
            built++;
        }
        double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        int live = 0, held = 0;
        foreach (Block block in _blockList)
        {
            if (block.Mesh == null)
                continue;
            live++;
            held += block.Parts.Count;
        }
        Note?.Invoke("thermal window glass: " + built + " block(s) made in " + ms.ToString("F1") + " ms; " + live + " blocks of " + BlockSize.ToString("F0") + " m hold " + held + " still panes, "
            + _single.Count + " drawn on their own (they can move, or are switched off; " + _notRead + " could not be read), " + _broken.Count + " broken ones by their pieces (" + _wasBroken + " were broken as the level loaded); reading the panes took " + _readMs.ToString("F0") + " ms in all");
    }

    // once a frame (the plugin's Update): the blocks made once no scene has come in for a second
    internal static void Tick()
    {
        if (_blocksDirty && Time.unscaledTime >= _buildAfter)
            BuildDirty();
    }

    // A window the game has broken: what is still stuck in its frame, as the game's own CombineStuckPieces makes it
    // (one mesh in the window's own frame, what the game shows itself where its WindowsManager is off), in the world.
    // Empty: nothing left in the frame. Null: could not be had (then the pieces are drawn one by one, as before).
    private static readonly AccessTools.FieldRef<EFT.Interactive.WindowBreaker, Vector2> AddOf =
        AccessTools.FieldRefAccess<EFT.Interactive.WindowBreaker, Vector2>("_add");
    private static readonly Vector3[] NoTriangles = new Vector3[0];
    private static int _stuckFailed;
    private static Vector3[]? StuckTriangles(EFT.Interactive.WindowBreaker breaker, Bounds where)
    {
        Mesh? mesh = null;
        try
        {
            EFT.Interactive.WindowBreaker.Piece[]? pieces = PiecesOf(breaker);
            if (pieces == null)
                return null;
            bool any = false;
            foreach (EFT.Interactive.WindowBreaker.Piece piece in pieces)
                if (piece != null && piece.Stuck)
                {
                    any = true;
                    break;
                }
            if (!any)
                return NoTriangles;
            mesh = EFT.Interactive.WindowBreaker.CombineStuckPieces(pieces, AddOf(breaker), breaker.AxisX, breaker.AxisY, breaker.AxisZ, breaker.ZSurfs);
            Matrix4x4 toWorld = (breaker.Renderer != null ? breaker.Renderer.transform : breaker.transform).localToWorldMatrix;
            Vector3[] vertices = mesh.vertices;
            int[] indices = mesh.triangles;
            var triangles = new Vector3[indices.Length - indices.Length % 3];
            const float slack = 0.15f;
            Vector3 lo = where.min, hi = where.max;
            for (int i = 0; i < triangles.Length; i++)
            {
                Vector3 p = toWorld.MultiplyPoint3x4(vertices[indices[i]]);
                if (p.x < lo.x - slack || p.y < lo.y - slack || p.z < lo.z - slack || p.x > hi.x + slack || p.y > hi.y + slack || p.z > hi.z + slack)
                {
                    if (_stuckFailed++ < 4)
                        Note?.Invoke("thermal window glass: the stuck pieces of " + breaker.name + ", merged, lie outside its pane (" + p.ToString("F2") + " against " + lo.ToString("F2") + ".." + hi.ToString("F2") + "): drawn one by one");
                    return null;
                }
                triangles[i] = p;
            }
            return triangles;
        }
        catch (System.Exception e)
        {
            if (_stuckFailed++ < 4)
                Note?.Invoke("thermal window glass: the stuck pieces of " + breaker.name + " not merged (" + e.GetType().Name + ": " + e.Message + "): drawn one by one");
            return null;
        }
        finally
        {
            if (mesh != null)
                Object.Destroy(mesh);
        }
    }

    // the game changed a window's pieces (broken, a piece fallen): its block made again with what is left in the frame
    private static void AfterUpdateMesh(EFT.Interactive.WindowBreaker __instance)
    {
        if (__instance == null || !_byBreaker.TryGetValue(__instance, out Part? part) || part.Renderer == null)
            return;
        if (part.InBlock && !part.Broken)
        {
            part.Triangles = StuckTriangles(__instance, part.Where);
            if (part.Triangles == null)
            {
                part.Broken = true;
                _broken.Add(part);
            }
            if (_blocks.TryGetValue(part.Block, out Block? block))
            {
                block.Dirty = true;
                _blocksDirty = true;
                _buildAfter = 0f;
            }
            return;
        }
        if (!part.Broken)
        {
            part.Broken = true;
            _broken.Add(part);
            _single.Remove(part);
        }
    }

    // a scene left: what is gone is dropped everywhere, its blocks made again from what is left
    private static void Forget()
    {
        Decals.RemoveAll(DecalGone);
        Glass.RemoveAll(GlassGone);
        _single.RemoveAll(GlassGone);
        _broken.RemoveAll(GlassGone);
        _gone = false;
        _byBreaker.Clear();
        _merged = 0;
        foreach (Part part in Glass)
        {
            if (part.Breaker != null)
                _byBreaker[part.Breaker] = part;
            if (part.Triangles != null && !part.Broken)
                _merged++;
        }
        foreach (Block block in _blockList)
            foreach (Part part in block.Parts)
                if (part.Renderer == null)
                {
                    block.Dirty = true;
                    _blocksDirty = true;
                    break;
                }
        _buildAfter = Time.unscaledTime + 1f;
    }

    internal static void Hook()
    {
        if (_hooked)
            return;
        _hooked = true;
        SceneManager.sceneLoaded += (scene, mode) => Collect(scene);
        SceneManager.sceneUnloaded += scene => Forget();
        try
        {
            var broke = AccessTools.Method(typeof(EFT.Interactive.WindowBreaker), "UpdateMesh");
            if (broke != null)
                new Harmony("justachae.thermalscene." + typeof(ThermalScene).Namespace).Patch(broke, postfix: new HarmonyMethod(typeof(ThermalScene), nameof(AfterUpdateMesh)));
            else
                Note?.Invoke("thermal window glass: the game's WindowBreaker.UpdateMesh not found, a window broken while playing keeps its whole pane in the thermal picture");
        }
        catch (System.Exception e)
        {
            Note?.Invoke("thermal window glass: WindowBreaker.UpdateMesh not hooked: " + e.Message);
        }
        for (int i = 0; i < SceneManager.sceneCount; i++)
            Collect(SceneManager.GetSceneAt(i));
    }

    // the see-through shaders the T-7 shows solid: under the game's thermal switch (_ThermalVisionOn) their pixel
    // programs force the alpha to 1 (read from the game's own compiled shaders, 2026-11-26): the window glass (also
    // what the WindowsManager draws instanced), the other reflective glass (black, opaque) and DepthZwrite (its own
    // temperature, opaque). Others only change their colour (Standard, AlphaBumpedFresnel, WireNetting) or do not
    // react at all (Custom/Glass2), so the T-7 sees through them, and so does the thermal lens here.
    private static readonly HashSet<string> SolidForThermal = new HashSet<string>
    {
        "Global Fog/Transparent Reflective Specular",
        "p0/Transparent/Reflective/Specular",
        "Transparent/DepthZwrite",
    };

    private static void Collect(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;
        int decals = 0, glass = 0;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                _found.Clear();
                root.GetComponentsInChildren(true, _found);
                foreach (Renderer r in _found)
                {
                    if (r == null || r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer)
                        continue;
                    _materials.Clear();
                    r.GetSharedMaterials(_materials);
                    if (_materials.Count == 0)
                        continue;
                    bool allDecal = true;
                    for (int sub = 0; sub < _materials.Count; sub++)
                    {
                        Material m = _materials[sub];
                        string shader = m != null && m.shader != null ? m.shader.name : "";
                        if (shader.IndexOf("decal", System.StringComparison.OrdinalIgnoreCase) < 0)
                            allDecal = false;
                        if (r is MeshRenderer && m != null && SolidForThermal.Contains(shader))
                        {
                            // (by its mesh: for the WindowsManager's windows the game has switched the renderer off)
                            Mesh? mesh = r is MeshRenderer && r.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                            int at = r.isPartOfStaticBatch ? ((MeshRenderer)r).subMeshStartIndex + sub : sub;
                            if (mesh == null || at >= mesh.subMeshCount)
                                continue;
                            r.TryGetComponent(out EFT.Interactive.WindowBreaker breaker);
                            var part = new Part { Renderer = r, Sub = at, Mesh = mesh, Breaker = breaker };
                            Glass.Add(part);
                            glass++;
                            if (breaker != null)
                                _byBreaker[breaker] = part;
                            part.Where = r.bounds;
                            part.Near = part.Where;
                            part.Near.Expand(4f);
                            Rigidbody body = r.GetComponentInParent<Rigidbody>(true);
                            part.Free = body != null && !body.isKinematic;
                            // (what stands still: batched with the level or marked static (the game's levels are neither:
                            // 0 of Ground Zero's 7490 panes, 0.1.48), else nothing above it that the game moves: no door
                            // or lid (WorldInteractiveObject), no body of its own (Rigidbody), nothing animated)
                            bool still = r.gameObject.activeInHierarchy && (r.isPartOfStaticBatch || r.gameObject.isStatic || Still(r.transform));
                            Bounds where = part.Where;
                            bool damaged = breaker != null && breaker.IsDamaged;
                            if (damaged)
                            {
                                // (broken already as the level loaded: what is stuck in its frame)
                                part.Triangles = still ? StuckTriangles(breaker!, where) : null;
                                _wasBroken++;
                            }
                            else
                                part.Triangles = still ? TrianglesOf(mesh, at, r.isPartOfStaticBatch ? Matrix4x4.identity : r.localToWorldMatrix, where) : null;
                            if (part.Triangles != null)
                            {
                                IntoBlock(part, where);
                                part.InBlock = true;
                                _merged++;
                            }
                            else if (damaged)
                            {
                                part.Broken = true;
                                _broken.Add(part);
                            }
                            else
                            {
                                _single.Add(part);
                                if (still) _notRead++; else _moving++;
                                if (still && _whyLogs < 4)
                                {
                                    _whyLogs++;
                                    Note?.Invoke("thermal window glass: " + r.name + " (mesh " + mesh.name + ", sub " + at + ") drawn on its own: " + _why);
                                }
                            }
                        }
                    }
                    if (allDecal)
                    {
                        Decals.Add(new Decal { Renderer = r, Bounds = r.bounds });
                        decals++;
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Note?.Invoke("thermal scene: " + scene.name + " not read: " + e.Message);
        }
        _found.Clear();
        _materials.Clear();
        _meshData.Clear();
        _readMs += (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        if (_blocksDirty)
            _buildAfter = Time.unscaledTime + 1f;
        if (decals > 0 || glass > 0)
            Note?.Invoke("thermal scene " + scene.name + ": " + decals + " decal renderer(s) left out of the thermal lens's view, " + glass + " window glass part(s) drawn solid for it (" + Decals.Count + " and " + Glass.Count + " in all)");
    }

    // The decals hidden for the lens camera's render (put back by ShowDecals right after it): those whose place lies in
    // the lens camera's view. (Not "those drawn last frame", Renderer.isVisible: a decal only the lens camera draws, on a
    // level of detail the main camera does not show at that distance, was drawn one frame, so hidden the next, so not
    // drawn, so shown again: on and off every other frame, 120 changes in 120 frames in the log. Far rubble's paint
    // flickered in the thermal picture from where the two cameras chose different levels, the user's "B" flicker.)
    private static readonly Plane[] _decalPlanes = new Plane[6];
    internal static int HideDecals(Camera lens)
    {
        _hidden.Clear();
        GeometryUtility.CalculateFrustumPlanes(lens, _decalPlanes);
        foreach (Decal d in Decals)
        {
            if (!GeometryUtility.TestPlanesAABB(_decalPlanes, d.Bounds))
                continue;
            Renderer r = d.Renderer;
            if (r == null)
            {
                _gone = true;
                continue;
            }
            if (!r.enabled || r.forceRenderingOff)
                continue;
            r.forceRenderingOff = true;
            _hidden.Add(r);
        }
        return _hidden.Count;
    }

    internal static void ShowDecals()
    {
        foreach (Renderer r in _hidden)
            if (r != null)
                r.forceRenderingOff = false;
        _hidden.Clear();
        if (_gone)
        {
            _gone = false;
            Decals.RemoveAll(DecalGone);
        }
    }

    // the window glass in the camera's view drawn with the given material's pass (an even colour, against the depth
    // bound); of a window the game's WindowBreaker has broken, the pieces still stuck in the frame (each its own mesh,
    // where the game placed it), not the whole pane (the user's: a pane partly broken let the whole of it through)
    private static readonly AccessTools.FieldRef<EFT.Interactive.WindowBreaker, EFT.Interactive.WindowBreaker.Piece[]> PiecesOf =
        AccessTools.FieldRefAccess<EFT.Interactive.WindowBreaker, EFT.Interactive.WindowBreaker.Piece[]>("_pieces");
    private static readonly Plane[] _planes = new Plane[6];
    private const float GlassReach = 400f;
    internal static int DrawGlass(CommandBuffer cb, Material material, int pass, Camera camera)
    {
        BuildDirty();
        GeometryUtility.CalculateFrustumPlanes(camera, _planes);
        Vector3 at = camera.transform.position;
        int n = 0;
        // the still panes, by their blocks
        foreach (Block block in _blockList)
        {
            if (block.Mesh == null || block.Bounds.SqrDistance(at) > GlassReach * GlassReach || !GeometryUtility.TestPlanesAABB(_planes, block.Bounds))
                continue;
            cb.DrawMesh(block.Mesh, Matrix4x4.identity, material, 0, pass);
            n++;
        }
        // what moves or could not be read, each on its own
        foreach (Part p in _single)
        {
            // (first by where it stood as its scene loaded, with room for a door's swing: no call into the engine for
            // the many that are far or out of view)
            if (!p.Free && (p.Near.SqrDistance(at) > GlassReach * GlassReach || !GeometryUtility.TestPlanesAABB(_planes, p.Near)))
                continue;
            Renderer r = p.Renderer;
            if (r == null || p.Mesh == null || !r.gameObject.activeInHierarchy)
                continue;
            Bounds b = r.bounds;
            if ((b.center - at).sqrMagnitude > GlassReach * GlassReach || !GeometryUtility.TestPlanesAABB(_planes, b))
                continue;
            cb.DrawMesh(p.Mesh, r.isPartOfStaticBatch ? Matrix4x4.identity : r.localToWorldMatrix, material, p.Sub, pass);
            n++;
        }
        // the broken windows' stuck pieces
        foreach (Part p in _broken)
        {
            if (p.Renderer == null || p.Breaker == null || !p.Renderer.gameObject.activeInHierarchy)
                continue;
            n += DrawPieces(cb, material, pass, p.Breaker, at);
        }
        return n;
    }

    private static int DrawPieces(CommandBuffer cb, Material material, int pass, EFT.Interactive.WindowBreaker breaker, Vector3 at)
    {
        EFT.Interactive.WindowBreaker.Piece[]? pieces = PiecesOf(breaker);
        if (pieces == null || (breaker.transform.position - at).sqrMagnitude > GlassReach * GlassReach)
            return 0;
        int n = 0;
        foreach (EFT.Interactive.WindowBreaker.Piece piece in pieces)
        {
            if (piece == null || !piece.Stuck)
                continue;
            MeshFilter? filter = piece.Description.MeshFilter;
            GameObject? go = piece.Description.GameObject;
            if (filter == null || go == null || !go.activeInHierarchy || filter.sharedMesh == null)
                continue;
            cb.DrawMesh(filter.sharedMesh, filter.transform.localToWorldMatrix, material, 0, pass);
            n++;
        }
        return n;
    }
}
