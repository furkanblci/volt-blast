#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using BlockBlast.Core;

/// <summary>
/// Records a scripted play sequence to numbered PNGs, for the README's animations.
///
/// Editor-only and never bootstrapped: it is attached by hand when a capture is wanted.
/// Driving the piece directly rather than faking touch input keeps the recording
/// repeatable, so a rerun after a visual change produces a comparable clip instead of a
/// different game.
/// </summary>
public class GifCapture : MonoBehaviour
{
    public string OutputDirectory = "";
    public int DragFrames = 26;
    public int SettleFrames = 64;

    private static readonly Color[] Hues =
    {
        new Color32(40, 214, 240, 255), new Color32(150, 240, 70, 255),
        new Color32(255, 72, 158, 255), new Color32(188, 122, 255, 255),
        new Color32(255, 178, 50, 255), new Color32(96, 158, 255, 255),
    };

    private IEnumerator Start()
    {
        if (string.IsNullOrEmpty(OutputDirectory)) yield break;

        Directory.CreateDirectory(OutputDirectory);
        foreach (string old in Directory.GetFiles(OutputDirectory, "*.png")) File.Delete(old);

        var grid = FindAnyObjectByType<GridManager>();
        var gm = FindAnyObjectByType<GameManager>();
        var spawn = FindAnyObjectByType<SpawnManager>();
        if (grid == null || gm == null || spawn == null) yield break;

        // Let the title intro finish so it does not sit over the recording.
        yield return new WaitForSeconds(2.2f);

        grid.ClearGrid();
        spawn.ClearTray();
        spawn.RefillTray();
        yield return null;

        BlockInstance piece = null;
        Vector2Int anchor = Vector2Int.zero;
        int bestRows = -1;

        foreach (BlockInstance p in spawn.Slots)
        {
            if (p == null || p.IsConsumed) continue;

            var rows = new HashSet<int>();
            foreach (Vector2Int c in p.Table.Cells) rows.Add(c.y);
            if (rows.Count <= bestRows) continue;

            bool placed = false;
            for (int ay = 0; ay < grid.GridHeight && !placed; ay++)
                for (int ax = 0; ax < grid.GridWidth && !placed; ax++)
                    if (grid.CanPlace(p.Table, ax, ay))
                    {
                        piece = p;
                        anchor = new Vector2Int(ax, ay);
                        bestRows = rows.Count;
                        placed = true;
                    }
        }

        if (piece == null) yield break;

        // Fill the whole board except the piece's target cells and a scatter of holes kept
        // out of the rows that are about to clear. The first take cleared three rows off a
        // half-empty board and left almost nothing behind: the clip was mostly an empty
        // grid, so the clear had nothing to read against. A dense board that stays dense is
        // what makes the two rows vanishing look like something happened.
        var occupied = new HashSet<Vector2Int>();
        var clearing = new HashSet<int>();
        foreach (Vector2Int c in piece.Table.Cells)
        {
            var w = new Vector2Int(anchor.x + c.x, anchor.y + c.y);
            occupied.Add(w);
            clearing.Add(w.y);
        }

        var holes = new HashSet<Vector2Int>();
        var rng = new System.Random(20260901);
        while (holes.Count < 15)
        {
            int hx = rng.Next(grid.GridWidth);
            int hy = rng.Next(grid.GridHeight);
            if (clearing.Contains(hy)) continue;          // never break a row that must clear
            holes.Add(new Vector2Int(hx, hy));
        }

        var single = new PlacementTable(new List<Vector2Int> { Vector2Int.zero }, grid.GridWidth, grid.GridHeight);
        for (int y = 0; y < grid.GridHeight; y++)
            for (int x = 0; x < grid.GridWidth; x++)
            {
                var cell = new Vector2Int(x, y);
                if (occupied.Contains(cell) || holes.Contains(cell)) continue;
                grid.TryPlace(single, x, y, Hues[(x * 3 + y * 5) % Hues.Length], out _);
            }

        if (ScoreManager.Instance != null) ScoreManager.Instance.ScoreTurn(400, 2);
        yield return null;

        Vector3 from = piece.transform.position;
        Vector3 to = grid.CellToWorld(anchor.x, anchor.y) + new Vector3(0.4f, 1.2f, 0f);
        piece.BeginDrag(from);

        int frame = 0;
        for (int k = 0; k < DragFrames; k++)
        {
            yield return new WaitForEndOfFrame();
            Vector3 p = Vector3.Lerp(from, to, Easing.OutCubic(k / (float)(DragFrames - 1)));
            piece.transform.position = p;
            piece.DragTo(p, Vector3.zero);
            Write(frame++);
        }

        piece.EndDrag();
        gm.TryCommitPlacement(piece, anchor);

        for (int k = 0; k < SettleFrames; k++)
        {
            yield return new WaitForEndOfFrame();
            Write(frame++);
        }

        Debug.Log($"[GifCapture] wrote {frame} frames to {OutputDirectory}");
    }

    private void Write(int index)
    {
        Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(OutputDirectory, $"f{index:000}.png"), tex.EncodeToPNG());
        Destroy(tex);
    }
}
#endif
