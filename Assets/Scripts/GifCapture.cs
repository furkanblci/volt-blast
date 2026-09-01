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
///
/// <see cref="Arm"/> defaults to false and must be set for every capture. It exists because
/// this component was once left attached to the scene with its output path still filled in,
/// and shipped: the game came up playing itself. A recorder that needs arming cannot be
/// forgotten into a build, only into a scene where it does nothing.
/// </summary>
public class GifCapture : MonoBehaviour
{
    [Tooltip("Must be ticked for each capture. Off is the only safe resting state for " +
             "something that drives the game on its own.")]
    public bool Arm;

    public string OutputDirectory = "";
    public int DragFrames = 26;
    public int SettleFrames = 64;

    private static readonly Color[] Hues =
    {
        new Color32(40, 214, 240, 255), new Color32(150, 240, 70, 255),
        new Color32(255, 72, 158, 255), new Color32(188, 122, 255, 255),
        new Color32(255, 178, 50, 255), new Color32(96, 158, 255, 255),
    };

    public int Placements = 4;

    private IEnumerator Start()
    {
        if (!Arm || string.IsNullOrEmpty(OutputDirectory)) yield break;

        Directory.CreateDirectory(OutputDirectory);
        foreach (string old in Directory.GetFiles(OutputDirectory, "*.png")) File.Delete(old);

        var grid = FindAnyObjectByType<GridManager>();
        var gm = FindAnyObjectByType<GameManager>();
        var spawn = FindAnyObjectByType<SpawnManager>();
        if (grid == null || gm == null || spawn == null) yield break;

        yield return new WaitForSeconds(2.2f);   // let the title intro finish

        grid.ClearGrid();
        spawn.ClearTray();
        spawn.RefillTray();
        SeedBoard(grid, 22);
        if (ScoreManager.Instance != null) ScoreManager.Instance.ScoreTurn(400, 2);
        yield return new WaitForSeconds(0.5f);   // let the seeding flare settle

        int frame = 0;

        // Several moves rather than one: a single drop is over before a viewer has worked
        // out what the game is. Every second placement is set up to clear, so the clip has
        // a rhythm of ordinary moves punctuated by the thing worth showing.
        for (int move = 0; move < Placements; move++)
        {
            BlockInstance piece = null;
            Vector2Int anchor = Vector2Int.zero;
            bool wantClear = move % 2 == 1;

            if (!ChoosePlacement(grid, spawn, wantClear, ref piece, ref anchor))
                if (!ChoosePlacement(grid, spawn, false, ref piece, ref anchor)) break;

            if (wantClear) CompleteRowsAround(grid, piece, anchor);
            yield return null;

            Vector3 from = piece.transform.position;
            Vector3 to = grid.CellToWorld(anchor.x, anchor.y) + new Vector3(0.4f, 1.2f, 0f);
            piece.BeginDrag(from);

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

            int settle = wantClear ? SettleFrames : SettleFrames / 3;
            for (int k = 0; k < settle; k++)
            {
                yield return new WaitForEndOfFrame();
                Write(frame++);
            }

            if (spawn.IsTrayEmpty) spawn.RefillTray();
        }

        Debug.Log($"[GifCapture] wrote {frame} frames to {OutputDirectory}");
    }

    /// <summary>Scatters colour over the board so a clear has something to read against.</summary>
    private void SeedBoard(GridManager grid, int cells)
    {
        var single = new PlacementTable(new List<Vector2Int> { Vector2Int.zero }, grid.GridWidth, grid.GridHeight);
        var rng = new System.Random(20260901);
        int placed = 0, guard = 0;

        while (placed < cells && guard++ < 400)
        {
            int x = rng.Next(grid.GridWidth);
            int y = rng.Next(grid.GridHeight);
            if (!grid.IsCellEmpty(x, y)) continue;
            if (grid.TryPlace(single, x, y, Hues[(x * 3 + y * 5) % Hues.Length], out _)) placed++;
        }
    }

    /// <summary>
    /// Picks a tray piece and a legal anchor. When a clear is wanted it prefers a piece
    /// spanning two or more rows, so completing them is worth watching.
    /// </summary>
    private bool ChoosePlacement(GridManager grid, SpawnManager spawn, bool preferTall,
                                 ref BlockInstance piece, ref Vector2Int anchor)
    {
        // int.MinValue, not -1: when a short piece is wanted the score is negative, so a
        // -1 floor rejected every candidate and the capture picked nothing at all.
        int best = int.MinValue;
        foreach (BlockInstance p in spawn.Slots)
        {
            if (p == null || p.IsConsumed) continue;

            var rows = new HashSet<int>();
            foreach (Vector2Int c in p.Table.Cells) rows.Add(c.y);
            int score = preferTall ? rows.Count : -rows.Count;
            if (score <= best) continue;

            for (int ay = 0; ay < grid.GridHeight; ay++)
                for (int ax = 0; ax < grid.GridWidth; ax++)
                    if (grid.CanPlace(p.Table, ax, ay))
                    {
                        piece = p;
                        anchor = new Vector2Int(ax, ay);
                        best = score;
                        goto nextPiece;
                    }
            nextPiece: ;
        }
        return piece != null;
    }

    /// <summary>Fills every row the piece will touch, except the cells it occupies.</summary>
    private void CompleteRowsAround(GridManager grid, BlockInstance piece, Vector2Int anchor)
    {
        var occupied = new HashSet<Vector2Int>();
        var rows = new HashSet<int>();
        foreach (Vector2Int c in piece.Table.Cells)
        {
            var w = new Vector2Int(anchor.x + c.x, anchor.y + c.y);
            occupied.Add(w);
            rows.Add(w.y);
        }

        var single = new PlacementTable(new List<Vector2Int> { Vector2Int.zero }, grid.GridWidth, grid.GridHeight);
        foreach (int y in rows)
            for (int x = 0; x < grid.GridWidth; x++)
            {
                if (occupied.Contains(new Vector2Int(x, y)) || !grid.IsCellEmpty(x, y)) continue;
                grid.TryPlace(single, x, y, Hues[(x * 3 + y * 5) % Hues.Length], out _);
            }
    }

    private void Write(int index)
    {
        Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(OutputDirectory, $"f{index:000}.png"), tex.EncodeToPNG());
        Destroy(tex);
    }
}
#endif
