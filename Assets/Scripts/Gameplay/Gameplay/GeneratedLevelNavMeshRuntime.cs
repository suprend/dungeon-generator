using System;
using System.Collections;
using NavMeshPlus.Components;
using NavMeshPlus.Extensions;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(GeneratedLevelRuntime))]
[RequireComponent(typeof(NavMeshSurface))]
[RequireComponent(typeof(CollectSources2d))]
public sealed class GeneratedLevelNavMeshRuntime : MonoBehaviour
{
    [SerializeField] private GeneratedLevelRuntime generatedLevelRuntime;
    [SerializeField] private NavMeshSurface navMeshSurface;
    [SerializeField] private bool rebuildOnGeneratedRoomsChanged = true;
    [SerializeField] private bool rotateSurfaceToXY = true;

    private Coroutine rebuildRoutine;

    public bool IsNavMeshReady { get; private set; }
    public event Action NavMeshBuilt;

    private void Awake()
    {
        ResolveComponents();
    }

    private void Reset()
    {
        ResolveComponents();
    }

    private void OnEnable()
    {
        ResolveComponents();
        if (generatedLevelRuntime != null)
            generatedLevelRuntime.GeneratedRoomsChanged += HandleGeneratedRoomsChanged;

        if (rebuildOnGeneratedRoomsChanged && generatedLevelRuntime != null && generatedLevelRuntime.LastGeneratedRooms.Count > 0)
            RebuildNavMesh();
    }

    private void OnDisable()
    {
        if (generatedLevelRuntime != null)
            generatedLevelRuntime.GeneratedRoomsChanged -= HandleGeneratedRoomsChanged;
    }

    public void RebuildNavMesh()
    {
        if (rebuildRoutine != null)
            StopCoroutine(rebuildRoutine);

        rebuildRoutine = StartCoroutine(RebuildNavMeshRoutine());
    }

    private void HandleGeneratedRoomsChanged()
    {
        if (rebuildOnGeneratedRoomsChanged)
            RebuildNavMesh();
    }

    private IEnumerator RebuildNavMeshRoutine()
    {
        IsNavMeshReady = false;
        Debug.Log("[GeneratedLevelNavMeshRuntime] Rebuild requested.");
        yield return null;
        yield return new WaitForFixedUpdate();

        Physics2D.SyncTransforms();

        if (navMeshSurface == null)
        {
            Debug.LogWarning("[GeneratedLevelNavMeshRuntime] NavMeshSurface is not assigned.");
            rebuildRoutine = null;
            yield break;
        }

        if (rotateSurfaceToXY)
            navMeshSurface.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);

        Debug.Log($"[GeneratedLevelNavMeshRuntime] Building navmesh. LayerMask={navMeshSurface.layerMask.value} UseGeometry={navMeshSurface.useGeometry} CollectObjects={navMeshSurface.collectObjects} Position={navMeshSurface.transform.position} Rotation={navMeshSurface.transform.rotation.eulerAngles}");
        navMeshSurface.BuildNavMesh();
        var navMeshData = navMeshSurface.navMeshData;
        if (navMeshData == null)
        {
            Debug.LogWarning("[GeneratedLevelNavMeshRuntime] BuildNavMesh finished but navMeshData is null.");
        }
        else
        {
            var bounds = navMeshData.sourceBounds;
            Debug.Log($"[GeneratedLevelNavMeshRuntime] BuildNavMesh finished. SourceBounds center={bounds.center} size={bounds.size}");
        }

        IsNavMeshReady = true;
        rebuildRoutine = null;
        NavMeshBuilt?.Invoke();
    }

    private void ResolveComponents()
    {
        if (generatedLevelRuntime == null)
            generatedLevelRuntime = GetComponent<GeneratedLevelRuntime>();
        if (navMeshSurface == null)
            navMeshSurface = GetComponent<NavMeshSurface>();
    }
}
