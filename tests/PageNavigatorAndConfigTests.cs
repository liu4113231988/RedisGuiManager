using System;
using System.Threading;
using System.Threading.Tasks;
using RedisGuiManager;
using Xunit;

namespace RedisGuiManager.Tests
{
    /// <summary>
    /// The offset is the contract between the navigator and the value editors: it must only stick
    /// when a page actually loaded, otherwise paging silently shows the wrong rows.
    /// </summary>
    public class PageNavigatorNavigationTests
    {
        private static PageNavigator WithImmediateHandler(out Func<Task> handler)
        {
            var page = new PageNavigator();
            handler = () => { page.UpdatePage(true); return Task.CompletedTask; };
            page.PageChanged += handler;
            return page;
        }

        [Fact]
        public async Task Committed_navigation_advances_the_offset()
        {
            using var page = WithImmediateHandler(out _);

            bool moved = await page.NavigateAsync(PageNavigator.PageSize);

            Assert.True(moved);
            Assert.Equal(PageNavigator.PageSize, page.Offset);
        }

        [Fact]
        public async Task Offset_rolls_back_when_no_handler_commits()
        {
            using var page = new PageNavigator();

            bool moved = await page.NavigateAsync(PageNavigator.PageSize);

            Assert.False(moved);
            Assert.Equal(0, page.Offset);
        }

        [Fact]
        public async Task Previous_page_cannot_go_before_the_start()
        {
            using var page = WithImmediateHandler(out _);
            await page.NavigateAsync(PageNavigator.PageSize);

            await page.NavigateAsync(-100);

            Assert.Equal(0, page.Offset);
        }

        [Fact]
        public async Task CanNavigate_veto_blocks_the_move()
        {
            using var page = WithImmediateHandler(out _);
            await page.NavigateAsync(PageNavigator.PageSize);

            page.CanNavigate = () => false;
            bool moved = await page.NavigateAsync(PageNavigator.PageSize * 2);

            Assert.False(moved);
            Assert.Equal(PageNavigator.PageSize, page.Offset);
        }

        [Fact]
        public async Task Async_handler_is_awaited_before_the_offset_sticks()
        {
            using var page = new PageNavigator();
            var gate = new TaskCompletionSource<bool>();
            page.PageChanged += async () => { await gate.Task; page.UpdatePage(true); };

            Task<bool> pending = page.NavigateAsync(PageNavigator.PageSize);

            // The handler has not finished, so NavigateAsync must still be pending.
            Assert.False(pending.IsCompleted);

            gate.SetResult(true);
            Assert.True(await pending);
            Assert.Equal(PageNavigator.PageSize, page.Offset);
        }

        [Fact]
        public async Task Uncommitted_offset_is_rolled_back_after_the_handler_returns()
        {
            using var page = new PageNavigator();
            Func<Task> commit = () => { page.UpdatePage(true); return Task.CompletedTask; };
            page.PageChanged += commit;
            await page.NavigateAsync(PageNavigator.PageSize);

            // A handler that loads nothing must leave the navigator on the previous page.
            page.PageChanged -= commit;
            page.PageChanged += () => Task.CompletedTask;

            bool moved = await page.NavigateAsync(PageNavigator.PageSize * 2);

            Assert.False(moved);
            Assert.Equal(PageNavigator.PageSize, page.Offset);
        }

        [Fact]
        public async Task Reentrant_navigation_is_ignored_instead_of_interleaving()
        {
            using var page = new PageNavigator();
            var gate = new TaskCompletionSource<bool>();
            page.PageChanged += async () => { await gate.Task; page.UpdatePage(true); };

            Task<bool> first = page.NavigateAsync(PageNavigator.PageSize);

            // A second click while the first page is still loading must be dropped.
            bool second = await page.NavigateAsync(PageNavigator.PageSize * 2);

            Assert.False(second);

            gate.SetResult(true);
            Assert.True(await first);
            Assert.Equal(PageNavigator.PageSize, page.Offset);
        }

        [Fact]
        public async Task Handler_exception_is_reported_through_LoadFailed_and_restores_the_offset()
        {
            using var page = new PageNavigator();
            Func<Task> commit = () => { page.UpdatePage(true); return Task.CompletedTask; };
            page.PageChanged += commit;
            await page.NavigateAsync(PageNavigator.PageSize);

            // The click handlers are async void, so an exception escaping NavigateAsync would land on
            // Application.ThreadException and end the process. It is surfaced through LoadFailed
            // instead, and the offset stays on the page that is actually on screen.
            Exception reported = null;
            page.LoadFailed += ex => reported = ex;
            page.PageChanged -= commit;
            page.PageChanged += () => throw new InvalidOperationException("load failed");

            bool moved = await page.NavigateAsync(PageNavigator.PageSize * 2);

            Assert.False(moved);
            Assert.IsType<InvalidOperationException>(reported);
            Assert.Equal(PageNavigator.PageSize, page.Offset);
        }

        [Fact]
        public async Task LoadFailed_is_not_raised_when_the_page_loads()
        {
            using var page = new PageNavigator();
            bool failed = false;
            page.LoadFailed += _ => failed = true;
            page.PageChanged += () => { page.UpdatePage(true); return Task.CompletedTask; };

            await page.NavigateAsync(PageNavigator.PageSize);

            Assert.False(failed);
        }

        [Fact]
        public async Task Reset_returns_to_the_first_page()
        {
            using var page = WithImmediateHandler(out _);
            await page.NavigateAsync(PageNavigator.PageSize);

            page.Reset();

            Assert.Equal(0, page.Offset);
        }

        [Fact]
        public void PageSize_is_500()
        {
            Assert.Equal(500, PageNavigator.PageSize);
        }
    }

    public class ConfigPersistenceTests : IDisposable
    {
        private readonly string sandbox = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "rgm-config-" + Guid.NewGuid().ToString("N"));

        public ConfigPersistenceTests()
        {
            System.IO.Directory.CreateDirectory(sandbox);
            // Redirect the settings path so the test never touches the real user settings file.
            Config.OverridePath = System.IO.Path.Combine(sandbox, "config.json");
            ResetToDefaults();
        }

        public void Dispose()
        {
            Config.OverridePath = null;
            try { System.IO.Directory.Delete(sandbox, true); } catch { }
        }

        private static void ResetToDefaults()
        {
            Config.dp_type = 0;
            Config.darkmode = 0;
            Config.mainform_is_maximized = 0;
            Config.mainform_pos_x = 0;
            Config.mainform_pos_y = 0;
            Config.mainform_width = 1159;
            Config.mainform_height = 712;
        }

        [Fact]
        public void Round_trips_every_persisted_field()
        {
            Config.dp_type = 3;
            Config.darkmode = 1;
            Config.mainform_is_maximized = 1;
            Config.mainform_pos_x = 11;
            Config.mainform_pos_y = 22;
            Config.mainform_width = 1234;
            Config.mainform_height = 777;
            Config.Save();

            Assert.True(System.IO.File.Exists(Config.ConfigPath));

            ResetToDefaults();
            Config.Load();

            Assert.Equal(3, Config.dp_type);
            Assert.Equal(1, Config.darkmode);
            Assert.Equal(1, Config.mainform_is_maximized);
            Assert.Equal(11, Config.mainform_pos_x);
            Assert.Equal(22, Config.mainform_pos_y);
            Assert.Equal(1234, Config.mainform_width);
            Assert.Equal(777, Config.mainform_height);
        }

        [Fact]
        public void Save_keeps_a_backup_of_the_previous_version()
        {
            string backup = Config.ConfigPath + ".bak";

            Config.mainform_width = 1000;
            Config.Save();
            Assert.False(System.IO.File.Exists(backup), "First save should not create a backup");

            Config.mainform_width = 2000;
            Config.Save();

            Assert.True(System.IO.File.Exists(backup), "Overwriting settings must keep a .bak");
            Assert.Contains("1000", System.IO.File.ReadAllText(backup));
            Assert.Contains("2000", System.IO.File.ReadAllText(Config.ConfigPath));
        }

        [Fact]
        public void Save_does_not_leave_a_temporary_file_behind()
        {
            Config.Save();

            Assert.False(System.IO.File.Exists(Config.ConfigPath + ".tmp"));
        }

        [Fact]
        public void Load_falls_back_to_the_backup_when_the_primary_is_corrupt()
        {
            Config.mainform_width = 1111;
            Config.Save();
            Config.mainform_width = 2222;
            Config.Save();

            // Corrupt only the primary; the .bak written by the second save still holds 1111.
            System.IO.File.WriteAllText(Config.ConfigPath, "{ this is not json");

            ResetToDefaults();
            Config.Load();

            Assert.Equal(1111, Config.mainform_width);
        }

        [Fact]
        public void Load_ignores_a_missing_file_without_throwing()
        {
            System.IO.File.Delete(Config.ConfigPath);
            System.IO.File.Delete(Config.ConfigPath + ".bak");

            Config.Load();

            // Defaults stay intact; the point is that no exception escapes.
            Assert.Equal(1159, Config.mainform_width);
        }

        [Fact]
        public void Load_keeps_defaults_for_a_truncated_file()
        {
            System.IO.File.WriteAllText(Config.ConfigPath, "{\"mainform_width\":");

            ResetToDefaults();
            Config.Load();

            Assert.Equal(1159, Config.mainform_width);
        }
    }
}