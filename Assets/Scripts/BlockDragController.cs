using System;
using UnityEngine;
using BlockBlast.Core;

/// <summary>
/// The one place pointer input is read.
///
/// Every tray piece used to run its own Update loop, poll Input and raycast, so a
/// single tap was resolved three times independently and whichever component happened
/// to update first won. There is now one handler that decides which piece was grabbed,
/// drives it, and hands the drop to <see cref="GameManager"/>.
///
/// The drop anchor is taken from the piece's own pivot, not from the pointer. The old
/// code converted the pointer position to a cell while the piece floated at an offset
/// above the finger, so the piece landed somewhere other than where it was drawn.
/// </summary>
[DefaultExecutionOrder(-50)]
public class BlockDragController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GridManager gridManager;
    [SerializeField] private SpawnManager spawnManager;
    [SerializeField] private GameManager gameManager;
    [SerializeField] private GhostPreview ghostPreview;
    /// <summary>Raised when a piece is lifted out of the tray.</summary>
    public event Action<BlockInstance> PieceGrabbed;

    /// <summary>Raised when a drop could not be placed and the piece went back.</summary>
    public event Action<BlockInstance> PieceRejected;

    [SerializeField] private Camera worldCamera;

    [Tooltip("Extra grab radius around a piece, in cells. Makes small pieces finger-friendly.")]
    [SerializeField, Range(0f, 1f)] private float grabMargin = 0.35f;

    private BlockInstance dragged;
    private Vector3 grabOffset;
    private Vector2Int lastPreviewAnchor = new Vector2Int(int.MinValue, int.MinValue);
    private bool lastPreviewValid;

    private enum PointerPhase { None, Began, Held, Ended }

    private void Awake() => ResolveReferences();

    private void ResolveReferences()
    {
        if (gridManager == null) gridManager = FindAnyObjectByType<GridManager>();
        if (spawnManager == null) spawnManager = FindAnyObjectByType<SpawnManager>();
        if (gameManager == null) gameManager = FindAnyObjectByType<GameManager>();
        if (ghostPreview == null) ghostPreview = FindAnyObjectByType<GhostPreview>();
        if (worldCamera == null) worldCamera = Camera.main;
    }

    private void Update()
    {
        if (spawnManager == null || gridManager == null) return;

        PointerPhase phase = ReadPointer(out Vector3 pointerWorld);

        switch (phase)
        {
            case PointerPhase.Began:
                TryGrab(pointerWorld);
                break;

            case PointerPhase.Held:
                if (dragged != null) UpdateDrag(pointerWorld);
                break;

            case PointerPhase.Ended:
                if (dragged != null) Drop();
                break;

            case PointerPhase.None:
                // A drag can be orphaned if the pointer is lost (app backgrounded, touch
                // cancelled). Send the piece home rather than leaving it stranded.
                if (dragged != null) Cancel();
                break;
        }
    }

    // ---------- drag ----------

    private void TryGrab(Vector3 pointerWorld)
    {
        if (gameManager != null && gameManager.IsGameOver) return;

        BlockInstance best = null;
        float bestDistance = float.MaxValue;

        foreach (BlockInstance piece in spawnManager.Slots)
        {
            if (piece == null || piece.IsConsumed) continue;
            if (!piece.ContainsWorldPoint(pointerWorld, grabMargin)) continue;

            // Grab margins on adjacent slots can overlap; the nearest piece wins.
            float distance = ((Vector2)(piece.transform.position - pointerWorld)).sqrMagnitude;
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = piece;
        }

        if (best == null) return;

        dragged = best;
        grabOffset = dragged.BeginDrag(pointerWorld);
        PieceGrabbed?.Invoke(dragged);
        InvalidatePreview();
        UpdatePreview();
    }

    private void UpdateDrag(Vector3 pointerWorld)
    {
        dragged.DragTo(pointerWorld, grabOffset);
        UpdatePreview();
    }

    private void Drop()
    {
        BlockInstance piece = dragged;
        dragged = null;

        ClearPreview();
        piece.EndDrag();

        Vector2Int anchor = piece.CurrentAnchor;
        if (gameManager == null || !gameManager.TryCommitPlacement(piece, anchor))
        {
            piece.ReturnHome();
            // Only a real drop reports rejection. Cancel() also sends a piece home, but
            // that is input being interrupted, not the board saying no.
            PieceRejected?.Invoke(piece);
        }
    }

    private void Cancel()
    {
        BlockInstance piece = dragged;
        dragged = null;

        ClearPreview();
        piece.EndDrag();
        piece.ReturnHome();
    }

    // ---------- preview ----------

    /// <summary>
    /// Repaints the ghost only when the snapped anchor or its validity actually changes.
    /// The old preview tore down and rebuilt its cells every frame of every drag.
    /// </summary>
    private void UpdatePreview()
    {
        if (ghostPreview == null || dragged == null) return;

        Vector2Int anchor = dragged.CurrentAnchor;
        bool valid = gridManager.CanPlace(dragged.Table, anchor.x, anchor.y);

        if (anchor == lastPreviewAnchor && valid == lastPreviewValid) return;

        lastPreviewAnchor = anchor;
        lastPreviewValid = valid;

        if (!valid && !gridManager.IsInsideGrid(anchor.x, anchor.y))
        {
            ghostPreview.Hide();
            return;
        }

        ghostPreview.Show(dragged.Table, anchor, valid);
    }

    private void ClearPreview()
    {
        InvalidatePreview();
        if (ghostPreview != null) ghostPreview.Hide();
    }

    private void InvalidatePreview()
    {
        lastPreviewAnchor = new Vector2Int(int.MinValue, int.MinValue);
        lastPreviewValid = false;
    }

    // ---------- pointer ----------

    /// <summary>
    /// Collapses mouse and touch into one stream. Only the first touch is honoured, so a
    /// second finger cannot yank a piece mid-drag.
    /// </summary>
    private PointerPhase ReadPointer(out Vector3 world)
    {
        world = Vector3.zero;

        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            world = ScreenToWorld(touch.position);

            switch (touch.phase)
            {
                case TouchPhase.Began: return PointerPhase.Began;
                case TouchPhase.Moved:
                case TouchPhase.Stationary: return PointerPhase.Held;
                case TouchPhase.Ended: return PointerPhase.Ended;
                default: return PointerPhase.None; // Canceled
            }
        }

        world = ScreenToWorld(Input.mousePosition);

        if (Input.GetMouseButtonDown(0)) return PointerPhase.Began;
        if (Input.GetMouseButtonUp(0)) return PointerPhase.Ended;
        if (Input.GetMouseButton(0)) return PointerPhase.Held;

        return PointerPhase.None;
    }

    private Vector3 ScreenToWorld(Vector3 screenPosition)
    {
        if (worldCamera == null)
        {
            worldCamera = Camera.main;
            if (worldCamera == null) return Vector3.zero;
        }

        // Distance from the camera to the z=0 play plane, so orthographic and
        // perspective cameras both land on the board.
        screenPosition.z = Mathf.Abs(worldCamera.transform.position.z);
        Vector3 world = worldCamera.ScreenToWorldPoint(screenPosition);
        world.z = 0f;
        return world;
    }
}
