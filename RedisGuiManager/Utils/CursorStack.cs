using System.Collections.Generic;
using StackExchange.Redis;

namespace RedisGuiManager
{
    /// <summary>One HSCAN page plus the cursor to resume from.</summary>
    public readonly struct HashScanPage
    {
        public string NextCursor { get; }
        public List<HashEntry> Entries { get; }

        public HashScanPage(string nextCursor, List<HashEntry> entries)
        {
            NextCursor = nextCursor;
            Entries = entries;
        }
    }

    /// <summary>One SSCAN page plus the cursor to resume from.</summary>
    public readonly struct SetScanPage
    {
        public string NextCursor { get; }
        public List<RedisValue> Members { get; }

        public SetScanPage(string nextCursor, List<RedisValue> members)
        {
            NextCursor = nextCursor;
            Members = members;
        }
    }

    /// <summary>One XRANGE page plus whether another page follows.</summary>
    public readonly struct StreamPage
    {
        public bool HasMore { get; }
        public List<StreamEntry> Entries { get; }

        public StreamPage(bool hasMore, List<StreamEntry> entries)
        {
            HasMore = hasMore;
            Entries = entries;
        }
    }

    /// <summary>
    /// Keeps the resume cursor for every visited page so paging forward never has to replay the
    /// scan from the start, and paging backward reuses a cursor that was already seen.
    /// Only one cursor per page boundary is stored, which is negligible memory.
    /// </summary>
    public sealed class CursorStack<T>
    {
        private readonly List<T> cursors = new List<T>();

        /// <summary>Number of page boundaries currently remembered.</summary>
        public int Count => cursors.Count;

        /// <summary>Drops all history and restarts from <paramref name="start"/> (used when the key or filter changes).</summary>
        public void Reset(T start)
        {
            cursors.Clear();
            cursors.Add(start);
        }

        public bool HasCursor(int pageIndex) => pageIndex >= 0 && pageIndex < cursors.Count;

        public T Get(int pageIndex)
        {
            if (HasCursor(pageIndex) == false)
            {
                throw new System.ArgumentOutOfRangeException(nameof(pageIndex),
                    $"No cursor retained for page {pageIndex}; the key or filter changed and paging must restart.");
            }

            return cursors[pageIndex];
        }

        /// <summary>Records the cursor that begins <paramref name="pageIndex"/>.</summary>
        public void Set(int pageIndex, T cursor)
        {
            while (cursors.Count <= pageIndex) cursors.Add(default);
            cursors[pageIndex] = cursor;
        }

        /// <summary>Forgets cursors beyond <paramref name="pageCount"/>, e.g. after a refresh.</summary>
        public void TrimTo(int pageCount)
        {
            if (pageCount >= 0 && cursors.Count > pageCount)
            {
                cursors.RemoveRange(pageCount, cursors.Count - pageCount);
            }
        }
    }
}