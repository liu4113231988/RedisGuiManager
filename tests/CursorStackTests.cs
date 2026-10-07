using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RedisGuiManager;
using Xunit;

namespace RedisGuiManager.Tests
{
    public class CursorStackTests
    {
        [Fact]
        public void New_stack_has_no_cursors()
        {
            var cursors = new CursorStack<string>();
            Assert.Equal(0, cursors.Count);
            Assert.False(cursors.HasCursor(0));
        }

        [Fact]
        public void Reset_seeds_the_start_cursor()
        {
            var cursors = new CursorStack<string>();
            cursors.Reset("0");
            Assert.True(cursors.HasCursor(0));
            Assert.Equal("0", cursors.Get(0));
        }

        [Fact]
        public void Get_outside_the_retained_range_throws()
        {
            var cursors = new CursorStack<string>();
            cursors.Reset("0");
            Assert.False(cursors.HasCursor(5));
            Assert.False(cursors.HasCursor(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => cursors.Get(5));
        }

        [Fact]
        public void Set_records_each_page_boundary()
        {
            var cursors = new CursorStack<string>();
            cursors.Reset("0");
            cursors.Set(1, "17");
            cursors.Set(2, "42");

            Assert.Equal("0", cursors.Get(0));
            Assert.Equal("17", cursors.Get(1));
            Assert.Equal("42", cursors.Get(2));
            Assert.Equal(3, cursors.Count);
        }

        [Fact]
        public void Set_fills_gaps_with_default_so_indexes_stay_aligned()
        {
            var cursors = new CursorStack<string>();
            cursors.Reset("0");
            cursors.Set(3, "99");

            Assert.True(cursors.HasCursor(3));
            Assert.Equal("99", cursors.Get(3));
            Assert.Null(cursors.Get(1));
        }

        [Fact]
        public void TrimTo_keeps_the_visited_prefix_and_drops_the_rest()
        {
            var cursors = new CursorStack<string>();
            cursors.Reset("0");
            cursors.Set(1, "17");
            cursors.Set(2, "42");
            cursors.Set(3, "77");

            cursors.TrimTo(2);

            Assert.Equal("0", cursors.Get(0));
            Assert.Equal("17", cursors.Get(1));
            Assert.False(cursors.HasCursor(2));
            Assert.Equal(2, cursors.Count);
        }

        [Fact]
        public void TrimTo_is_a_no_op_when_nothing_is_beyond_the_limit()
        {
            var cursors = new CursorStack<string>();
            cursors.Reset("0");
            cursors.Set(1, "17");

            cursors.TrimTo(5);
            cursors.TrimTo(2);

            Assert.Equal(2, cursors.Count);
            Assert.Equal("17", cursors.Get(1));
        }

        [Fact]
        public void TrimTo_ignores_negative_limits()
        {
            var cursors = new CursorStack<string>();
            cursors.Reset("0");
            cursors.Set(1, "17");

            cursors.TrimTo(-1);

            Assert.Equal(2, cursors.Count);
        }

        [Fact]
        public void Reset_discards_all_previous_cursors()
        {
            var cursors = new CursorStack<string>();
            cursors.Reset("0");
            cursors.Set(1, "17");
            cursors.Set(2, "42");

            cursors.Reset("start");

            Assert.Equal(1, cursors.Count);
            Assert.Equal("start", cursors.Get(0));
            Assert.False(cursors.HasCursor(1));
        }

        [Fact]
        public void Works_with_non_string_cursor_types()
        {
            var cursors = new CursorStack<long>();
            cursors.Reset(0L);
            cursors.Set(1, 99L);

            Assert.Equal(0L, cursors.Get(0));
            Assert.Equal(99L, cursors.Get(1));
        }
    }

    public class PageReadTests
    {
        [Fact]
        public void Reads_one_page_plus_one_so_the_caller_can_detect_more()
        {
            using var page = new PageNavigator();
            var values = page.Read(Enumerable.Range(0, 2000), CancellationToken.None);

            Assert.Equal(PageNavigator.PageSize + 1, values.Length);
            Assert.Equal(0, values[0]);
        }

        [Fact]
        public async System.Threading.Tasks.Task Offset_skips_the_earlier_pages()
        {
            using var page = new PageNavigator();
            Func<System.Threading.Tasks.Task> commit = () => { page.UpdatePage(true); return System.Threading.Tasks.Task.CompletedTask; };
            page.PageChanged += commit;

            await page.NavigateAsync(PageNavigator.PageSize);

            Assert.Equal(PageNavigator.PageSize, page.Offset);
            var values = page.Read(Enumerable.Range(0, 2000), CancellationToken.None);
            Assert.Equal(PageNavigator.PageSize, values[0]);
        }

        [Fact]
        public void Empty_source_yields_nothing()
        {
            using var page = new PageNavigator();
            Assert.Empty(page.Read(Array.Empty<int>(), CancellationToken.None));
        }

        [Fact]
        public void Cancellation_is_observed()
        {
            using var page = new PageNavigator();
            Assert.Throws<OperationCanceledException>(
                () => page.Read(Enumerable.Range(0, 100), new CancellationToken(true)));
        }

        [Fact]
        public void Reads_no_more_than_one_page_from_a_short_source()
        {
            using var page = new PageNavigator();
            var values = page.Read(Enumerable.Range(0, 10), CancellationToken.None);
            Assert.Equal(10, values.Length);
        }
    }
}