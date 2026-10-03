using ScrollOS.Core.Lifecycle;
using ScrollOS.Core.Render;
using ScrollOS.Core.Timeline;
using ScrollOS.Protocol;

namespace ScrollOS.Core.Ui;

public enum UiActionKind { Resume, Close, FocusApp, AppHit, FocusPrompt, Artifact }

public sealed record UiAction(UiActionKind Kind, long Entry = 0, Hit? Hit = null);

/// <summary>A clickable screen region. When regions overlap, the one added last wins.</summary>
public sealed record UiHit(Rect Rect, UiAction Action);

/// <summary>
/// Draws the timeline as one continuous scrollable document. Entries are laid out from the bottom up,
/// so only entries that are on screen get loaded from disk and rendered.
/// </summary>
public sealed class TimelineView(TimelineStore store, IReadOnlyDictionary<long, AppSession> sessions)
{
    const int MaxCachedBlocks = 256;
    const int Gutter = 2;

    sealed class Block(int width)
    {
        public int Width { get; } = width;
        public Canvas Canvas { get; } = new(width);
        public List<UiHit> Hits { get; } = [];
        public (int X, int Y)? Cursor { get; set; }
    }

    readonly Dictionary<long, Block> cache = [];

    public void Invalidate(long id) => cache.Remove(id);

    public void InvalidateAll() => cache.Clear();

    /// <summary>
    /// Draws the area [top, top + height) of <paramref name="buffer"/>. <paramref name="scroll"/> is the number of
    /// rows scrolled up from the bottom; it is clamped so the view never scrolls past the first entry.
    /// Returns the cursor position if the focused app has a focused input on screen.
    /// </summary>
    public (int X, int Y)? Draw(CellBuffer buffer, int x0, int top, int width, int height, ref int scroll,
        long? focusedApp, List<UiHit> hits)
    {
        var entries = store.Entries;
        int bottom = top + height;
        var placed = new List<(Block Block, int Y)>();

        while (true)
        {
            placed.Clear();
            int y = bottom + scroll;
            bool reachedFirst = entries.Count == 0;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var block = GetBlock(entries[i], width, focusedApp);
                y -= block.Canvas.Height;
                placed.Add((block, y));
                if (y <= top) break;
                if (i == 0) reachedFirst = true;
            }

            // Content doesn't reach the top: either we scrolled too far up, or there isn't a screenful yet.
            int gap = y - top;
            if (reachedFirst && gap > 0)
            {
                if (scroll > 0)
                {
                    scroll = Math.Max(0, scroll - gap);
                    continue;
                }
                for (int i = 0; i < placed.Count; i++) placed[i] = (placed[i].Block, placed[i].Y - gap);
            }
            break;
        }

        (int X, int Y)? cursor = null;
        for (int p = placed.Count - 1; p >= 0; p--)
        {
            var (block, by) = placed[p];
            var rows = block.Canvas.Rows;
            for (int r = 0; r < rows.Count; r++)
            {
                int sy = by + r;
                if (sy >= top && sy < bottom) buffer.Blit(rows[r], x0, sy);
            }
            foreach (var hit in block.Hits)
            {
                var rect = hit.Rect.Offset(x0, by);
                if (rect.Y < bottom && rect.Y + rect.H > top) hits.Add(hit with { Rect = rect });
            }
            if (block.Cursor is { } c && by + c.Y >= top && by + c.Y < bottom)
                cursor = (x0 + c.X, by + c.Y);
        }
        return cursor;
    }

    Block GetBlock(TimelineEntry e, int width, long? focusedApp)
    {
        if (cache.TryGetValue(e.Id, out var cached) && cached.Width == width) return cached;
        if (cache.Count >= MaxCachedBlocks) cache.Clear();

        var block = new Block(width);
        switch (e.Kind)
        {
            case EntryKind.Session: BuildSession(e, block); break;
            case EntryKind.Prompt: BuildPrompt(e, block); break;
            case EntryKind.App: BuildApp(e, block, focusedApp == e.Id); break;
            default: BuildNotification(e, block); break;
        }
        cache[e.Id] = block;
        return block;
    }

    static void BuildSession(TimelineEntry e, Block b)
    {
        var style = new Style(Fg: Theme.Muted);
        var label = $"── ScrollOS · {e.Time.LocalDateTime:ddd d MMM yyyy HH:mm} ";
        b.Canvas.Fill(0, 0, b.Width, 1, '─', style);
        b.Canvas.Write(0, 0, label, style);
        b.Canvas.EnsureHeight(2);
    }

    static void BuildNotification(TimelineEntry e, Block b)
    {
        var style = new Style(Fg: e.Error ? Theme.ErrorFg : Theme.Accent);
        var lines = TextWrap.Wrap(e.Title, b.Width - 2);
        for (int i = 0; i < lines.Count; i++)
            b.Canvas.Write(i == 0 ? 0 : 2, i, (i == 0 ? "● " : "") + lines[i], style);
        b.Canvas.EnsureHeight(lines.Count + 1);
    }

    void BuildPrompt(TimelineEntry e, Block b)
    {
        var c = b.Canvas;
        var stamp = $" {e.Time.LocalDateTime:HH:mm} #{e.Id}";
        c.Write(b.Width - stamp.Length, 0, stamp, new Style(Fg: Theme.Muted));
        int x = c.Write(0, 0, $"PS {ShortPath(e.Cwd)}> ", new Style(Fg: Theme.Accent, Bold: true));
        c.Write(x, 0, TextWrap.Truncate(e.Title, b.Width - x - stamp.Length - 1), new Style(Bold: true));

        int y = 1;
        if (store.ReadText(e.Id, TimelineStore.OutputFile) is { Length: > 0 } output)
            foreach (var line in TextWrap.Wrap(output, b.Width))
                c.Write(0, y++, line, default);
        if (store.ReadText(e.Id, TimelineStore.ErrorFile) is { Length: > 0 } error)
            foreach (var line in TextWrap.Wrap(error, b.Width))
                c.Write(0, y++, line, new Style(Fg: Theme.ErrorFg));
        if (e.Status == EntryStatus.Running)
            c.Write(0, y++, "… running", new Style(Fg: Theme.Muted));
        c.EnsureHeight(y + 1);
    }

    void BuildApp(TimelineEntry e, Block b, bool focused)
    {
        var c = b.Canvas;
        bool live = e.Status == EntryStatus.Live;
        var session = live ? sessions.GetValueOrDefault(e.Id) : null;

        // Header: "─ #12 Notes · 09:05 · ↻ from #7 · ● live ──────── [ Close ]"
        var state = !live ? "closed" : session?.Closing == true ? "closing…" : "● live";
        var resumed = e.ResumedFrom is { } from ? $" · ↻ from #{from}" : "";
        var header = $" #{e.Id} {e.App ?? e.Title} · {e.Time.LocalDateTime:HH:mm}{resumed} · {state} ";
        var button = live ? "[ Close ]" : "[ Resume ]";
        var line = new Style(Fg: focused ? Theme.Accent : Theme.Border);
        c.Fill(0, 0, b.Width, 1, '─', line);
        c.Write(1, 0, header, new Style(Fg: live ? Theme.Accent : Theme.Muted, Bold: live), b.Width - button.Length - 2);
        int bx = Math.Max(0, b.Width - button.Length);
        c.Write(bx, 0, button, new Style(Fg: Style.Rgb(230, 233, 240), Bg: Theme.ButtonBg, Bold: true));

        // Body: the live UI, or the frozen snapshot of the app as it was when it closed.
        Widget? tree = live ? session?.Tree : LoadTree(e.Id);
        int bodyWidth = Math.Max(1, b.Width - Gutter);
        int y = 1;
        var appHits = new List<UiHit>();
        if (tree is null)
        {
            c.Write(Gutter, y++, live ? "starting…" : "(no snapshot)", new Style(Fg: Theme.Muted));
        }
        else
        {
            var context = session is null
                ? LayoutContext.Frozen
                : new LayoutContext { FocusedInput = focused ? session.FocusedInput : null, InputValues = session.Inputs };
            var layout = LayoutEngine.Render(tree, bodyWidth, context);
            c.Draw(layout.Canvas, Gutter, y, live ? null : s => s with { Dim = true });
            if (live)
            {
                foreach (var hit in layout.Hits)
                    appHits.Add(new UiHit(hit.Rect.Offset(Gutter, y), new UiAction(UiActionKind.AppHit, e.Id, hit)));
                if (layout.Cursor is { } cur) b.Cursor = (cur.X + Gutter, cur.Y + y);
            }
            y += layout.Canvas.Height;
        }

        var error = session?.Error ?? (live ? null : store.ReadText(e.Id, TimelineStore.ErrorFile));
        if (!string.IsNullOrEmpty(error))
            foreach (var errorLine in TextWrap.Wrap(error, bodyWidth))
                c.Write(Gutter, y++, errorLine, new Style(Fg: Theme.ErrorFg));

        var gutter = new Style(Fg: focused ? Theme.Accent : Theme.Border);
        for (int row = 1; row < y; row++) c.Put(0, row, focused ? '┃' : '│', gutter);
        c.EnsureHeight(y + 1);

        // Whole-block hits first so buttons and widgets (added later) take precedence.
        var body = new Rect(0, 0, b.Width, y);
        b.Hits.Add(new UiHit(body, new UiAction(live ? UiActionKind.FocusApp : UiActionKind.Artifact, e.Id)));
        b.Hits.Add(new UiHit(new Rect(bx, 0, button.Length, 1), new UiAction(live ? UiActionKind.Close : UiActionKind.Resume, e.Id)));
        b.Hits.AddRange(appHits);
    }

    Widget? LoadTree(long id)
    {
        var json = store.ReadText(id, TimelineStore.TreeFile);
        if (json is null) return null;
        try { return Wire.ParseTree(json); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    public static string ShortPath(string? cwd)
    {
        if (string.IsNullOrEmpty(cwd)) return "~";
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return cwd.StartsWith(profile, StringComparison.OrdinalIgnoreCase) ? "~" + cwd[profile.Length..] : cwd;
    }
}
