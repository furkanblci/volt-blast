using System;

namespace BlockBlast.Core
{
    /// <summary>
    /// The play field's data model. Deliberately free of MonoBehaviour, GameObject
    /// and Component so the whole rule set can be exercised from EditMode tests.
    ///
    /// Occupancy is a single ulong bitboard with a fixed stride of 8, which makes
    /// "does this piece fit" one AND instead of a per-cell loop. Colours are packed
    /// RGBA32 so a board round-trips through JSON without dragging UnityEngine.Color
    /// into the serialized payload.
    /// </summary>
    public sealed class BoardState
    {
        /// <summary>Row stride of the bitboard. Fixed at 8 so bit math stays branch-free.</summary>
        public const int Stride = 8;
        public const int CellCapacity = Stride * Stride;

        private readonly uint[] colors = new uint[CellCapacity];
        private ulong occupancy;

        public int Width { get; }
        public int Height { get; }

        /// <summary>Bit i is set when cell (i % 8, i / 8) is filled.</summary>
        public ulong Occupancy => occupancy;

        public BoardState(int width, int height)
        {
            if (width < 1 || width > Stride)
                throw new ArgumentOutOfRangeException(nameof(width), width, $"Width must be 1..{Stride}.");
            if (height < 1 || height > Stride)
                throw new ArgumentOutOfRangeException(nameof(height), height, $"Height must be 1..{Stride}.");

            Width = width;
            Height = height;
        }

        // ---------- bit helpers ----------

        public static int BitIndex(int x, int y) => (y << 3) + x;

        public static ulong BitAt(int x, int y) => 1UL << ((y << 3) + x);

        /// <summary>Bounds test. The unsigned cast folds the negative check into one comparison.</summary>
        public bool IsInside(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

        public bool IsOccupied(int x, int y) => IsInside(x, y) && (occupancy & BitAt(x, y)) != 0UL;

        public bool IsEmpty(int x, int y) => IsInside(x, y) && (occupancy & BitAt(x, y)) == 0UL;

        /// <summary>Packed RGBA32 of a filled cell, or 0 when empty / out of bounds.</summary>
        public uint GetColor(int x, int y) => IsInside(x, y) ? colors[BitIndex(x, y)] : 0u;

        // ---------- mutation ----------

        public void Fill(int x, int y, uint packedColor)
        {
            if (!IsInside(x, y)) return;
            occupancy |= BitAt(x, y);
            colors[BitIndex(x, y)] = packedColor;
        }

        public void Vacate(int x, int y)
        {
            if (!IsInside(x, y)) return;
            occupancy &= ~BitAt(x, y);
            colors[BitIndex(x, y)] = 0u;
        }

        /// <summary>Fills every cell in <paramref name="mask"/> with one colour.</summary>
        public void FillMask(ulong mask, uint packedColor)
        {
            occupancy |= mask;
            while (mask != 0UL)
            {
                colors[TrailingZeroCount(mask)] = packedColor;
                mask &= mask - 1UL;
            }
        }

        /// <summary>Empties every cell in <paramref name="mask"/>.</summary>
        public void ClearMask(ulong mask)
        {
            occupancy &= ~mask;
            while (mask != 0UL)
            {
                colors[TrailingZeroCount(mask)] = 0u;
                mask &= mask - 1UL;
            }
        }

        public void Clear()
        {
            occupancy = 0UL;
            Array.Clear(colors, 0, colors.Length);
        }

        public int FilledCount => PopCount(occupancy);

        // ---------- snapshot / restore ----------

        /// <summary>Copies the raw state out for saving. The array is a copy, not the live buffer.</summary>
        public void CopyTo(out ulong outOccupancy, uint[] destinationColors)
        {
            outOccupancy = occupancy;
            Array.Copy(colors, destinationColors, CellCapacity);
        }

        /// <summary>Overwrites the board wholesale. Bits outside the configured size are dropped.</summary>
        public void Restore(ulong newOccupancy, uint[] sourceColors)
        {
            occupancy = newOccupancy & SizeMask(Width, Height);
            if (sourceColors != null && sourceColors.Length >= CellCapacity)
                Array.Copy(sourceColors, colors, CellCapacity);
            else
                Array.Clear(colors, 0, colors.Length);
        }

        // ---------- static masks ----------

        /// <summary>Every playable cell for a board of this size.</summary>
        public static ulong SizeMask(int width, int height)
        {
            ulong row = (1UL << width) - 1UL;
            ulong mask = 0UL;
            for (int y = 0; y < height; y++) mask |= row << (y << 3);
            return mask;
        }

        public static ulong RowMask(int y, int width) => ((1UL << width) - 1UL) << (y << 3);

        public static ulong ColumnMask(int x, int height)
        {
            ulong mask = 0UL;
            for (int y = 0; y < height; y++) mask |= 1UL << ((y << 3) + x);
            return mask;
        }

        // ---------- bit intrinsics ----------
        // System.Numerics.BitOperations is not guaranteed on Unity's profile, so these
        // are spelled out. Both are branch-free and allocation-free.

        public static int PopCount(ulong v)
        {
            v -= (v >> 1) & 0x5555555555555555UL;
            v = (v & 0x3333333333333333UL) + ((v >> 2) & 0x3333333333333333UL);
            v = (v + (v >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
            return (int)((v * 0x0101010101010101UL) >> 56);
        }

        private static readonly int[] DeBruijnPositions =
        {
            0,  1,  2, 53,  3,  7, 54, 27, 4,  38, 41,  8, 34, 55, 48, 28,
            62,  5, 39, 46, 44, 42, 22,  9, 24, 35, 59, 56, 49, 18, 29, 11,
            63, 52,  6, 26, 37, 40, 33, 47, 61, 45, 43, 21, 23, 58, 17, 10,
            51, 25, 36, 32, 60, 20, 57, 16, 50, 31, 19, 15, 30, 14, 13, 12
        };

        /// <summary>Index of the lowest set bit. Undefined for 0, so callers must guard.</summary>
        public static int TrailingZeroCount(ulong v) =>
            DeBruijnPositions[((v & (ulong)(-(long)v)) * 0x022FDD63CC95386DUL) >> 58];
    }
}
