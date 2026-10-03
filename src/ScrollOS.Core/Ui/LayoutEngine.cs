using ScrollOS.Core.Render;
using ScrollOS.Protocol;

namespace ScrollOS.Core.Ui;

public enum HitKind { Click, Select, Focus }

/// <summary>A clickable region produced by layout: a button (Click), a list item (Select) or an input (Focus).</summary>
public sealed record Hit(Rect Rect, HitKind Kind, string Target, int Index = -1);

/// <summary>Runtime state that affects how a live app draws (typed-but-unsent input text, which input has focus).</summary>
public sealed class LayoutContext
{
    public static readonly LayoutContext Frozen = new();

    public string? FocusedInput { get; init; }
    public IReadOnlyDictionary<string, string>? InputValues { get; init; }
}

public sealed class LayoutResult(int width)
{
    public Canvas Canvas { get; } = new(width);
    public List<Hit> Hits { get; } = [];
    public (int X, int Y)? Cursor { get; set; }
}

/// <summary>
/// Lays out an app's widget tree into a canvas of a given width. Height is whatever the content needs,
/// because the timeline scrolls; apps never get a fixed-size window.
/// </summary>
public static class LayoutEngine
{
    const int MaxDepth = 64;
    const int RowGap = 1;

    public static LayoutResult Render(Widget root, int width, LayoutContext context)
    {
        var result = new LayoutResult(width);
        int height = Draw(root, result, context, 0, 0, width, 0);
        result.Canvas.EnsureHeight(height);
        return result;
    }

    /// <summary>Returns the first input widget's id in the tree, used to give a freshly rendered app keyboard focus.</summary>
    public static string? FirstInputId(Widget? w)
    {
        if (w is null) return null;
        if (IsType(w, "input")) return w.Id ?? "input";
        foreach (var child in w.Children ?? [])
            if (FirstInputId(child) is { } id) return id;
        return null;
    }

    public static bool ContainsInput(Widget? w, string id) =>
        w is not null && ((IsType(w, "input") && (w.Id ?? "input") == id) || (w.Children ?? []).Any(c => ContainsInput(c, id)));

    static bool IsType(Widget w, string type) => string.Equals(w.Type, type, StringComparison.OrdinalIgnoreCase);

    static int Draw(Widget w, LayoutResult r, LayoutContext ctx, int x, int y, int width, int depth)
    {
        if (width <= 0 || depth > MaxDepth) return 0;
        return (w.Type ?? "column").ToLowerInvariant() switch
        {
            "panel" => DrawPanel(w, r, ctx, x, y, width, depth),
            "row" => DrawRow(w, r, ctx, x, y, width, depth),
            "text" => DrawText(w, r, x, y, width),
            "button" => DrawButton(w, r, x, y, width),
            "list" => DrawList(w, r, x, y, width),
            "input" => DrawInput(w, r, ctx, x, y, width),
            "divider" => DrawDivider(r, x, y, width),
            _ => DrawColumn(w, r, ctx, x, y, width, depth),
        };
    }

    static int DrawColumn(Widget w, LayoutResult r, LayoutContext ctx, int x, int y, int width, int depth)
    {
        int cy = y;
        foreach (var child in w.Children ?? [])
            cy += Draw(child, r, ctx, x, cy, width, depth + 1);
        return cy - y;
    }

    static int DrawPanel(Widget w, LayoutResult r, LayoutContext ctx, int x, int y, int width, int depth)
    {
        if (width < 4) return DrawColumn(w, r, ctx, x, y, width, depth);

        var c = r.Canvas;
        var border = new Style(Fg: Color(w.Fg, Theme.Border));
        int innerHeight = Math.Max(1, DrawColumn(w, r, ctx, x + 2, y + 1, width - 4, depth));
        int height = innerHeight + 2;

        c.Put(x, y, '╭', border);
        c.Fill(x + 1, y, width - 2, 1, '─', border);
        c.Put(x + width - 1, y, '╮', border);
        if (!string.IsNullOrEmpty(w.Title))
            c.Write(x + 2, y, $" {w.Title} ", new Style(Fg: Theme.Accent, Bold: true), width - 4);

        for (int row = y + 1; row < y + height - 1; row++)
        {
            c.Put(x, row, '│', border);
            c.Put(x + width - 1, row, '│', border);
        }

        int bottom = y + height - 1;
        c.Put(x, bottom, '╰', border);
        c.Fill(x + 1, bottom, width - 2, 1, '─', border);
        c.Put(x + width - 1, bottom, '╯', border);
        return height;
    }

    /// <summary>
    /// Lays children out side by side. Buttons and single-line text keep their natural width;
    /// everything else shares the remaining space equally.
    /// </summary>
    static int DrawRow(Widget w, LayoutResult r, LayoutContext ctx, int x, int y, int width, int depth)
    {
        var kids = w.Children ?? [];
        if (kids.Count == 0) return 0;

        int available = width - RowGap * (kids.Count - 1);
        var natural = kids.Select(NaturalWidth).ToArray();
        int fixedTotal = natural.Sum(n => n ?? 0);
        int flexCount = natural.Count(n => n is null);

        int[] widths;
        if (fixedTotal + flexCount * 4 <= available)
        {
            int flex = flexCount > 0 ? (available - fixedTotal) / flexCount : 0;
            widths = natural.Select(n => n ?? flex).ToArray();
        }
        else
        {
            int each = Math.Max(0, available / kids.Count);
            widths = kids.Select(_ => each).ToArray();
        }

        int cx = x, height = 0;
        for (int i = 0; i < kids.Count; i++)
        {
            height = Math.Max(height, Draw(kids[i], r, ctx, cx, y, widths[i], depth + 1));
            cx += widths[i] + RowGap;
        }
        return height;
    }

    static int? NaturalWidth(Widget w) => (w.Type ?? "").ToLowerInvariant() switch
    {
        "button" => ButtonLabel(w).Length,
        "text" when w.Text is { } t && !t.Contains('\n') => t.Length,
        _ => null,
    };

    static int DrawText(Widget w, LayoutResult r, int x, int y, int width)
    {
        var style = new Style(
            Fg: Color(w.Fg, w.Dim == true ? Theme.Muted : 0),
            Bg: Color(w.Bg, 0),
            Bold: w.Bold == true);
        var lines = TextWrap.Wrap(w.Text ?? "", width);
        for (int i = 0; i < lines.Count; i++)
        {
            if (Style.IsSet(style.Bg)) r.Canvas.Fill(x, y + i, width, 1, ' ', style);
            r.Canvas.Write(x, y + i, lines[i], style, width);
        }
        r.Canvas.EnsureHeight(y + lines.Count);
        return lines.Count;
    }

    static string ButtonLabel(Widget w) => $"[ {w.Text ?? w.Id ?? "?"} ]";

    static int DrawButton(Widget w, LayoutResult r, int x, int y, int width)
    {
        var label = TextWrap.Truncate(ButtonLabel(w), width);
        var style = new Style(Fg: Color(w.Fg, Style.Rgb(230, 233, 240)), Bg: Color(w.Bg, Theme.ButtonBg), Bold: true);
        r.Canvas.Write(x, y, label, style, width);
        r.Hits.Add(new Hit(new Rect(x, y, label.Length, 1), HitKind.Click, w.Id ?? w.Text ?? ""));
        return 1;
    }

    static int DrawList(Widget w, LayoutResult r, int x, int y, int width)
    {
        var items = w.Items ?? [];
        if (items.Count == 0)
        {
            r.Canvas.Write(x, y, TextWrap.Truncate(w.Placeholder ?? "(empty)", width), new Style(Fg: Theme.Muted), width);
            return 1;
        }

        int selected = w.Selected ?? -1;
        var id = w.Id ?? "list";
        for (int i = 0; i < items.Count; i++)
        {
            bool isSelected = i == selected;
            var style = isSelected
                ? new Style(Fg: Color(w.Fg, 0), Bg: Theme.SelectedBg, Bold: true)
                : new Style(Fg: Color(w.Fg, 0));
            if (isSelected) r.Canvas.Fill(x, y + i, width, 1, ' ', style);
            r.Canvas.Write(x, y + i, (isSelected ? "▸ " : "  ") + TextWrap.Truncate(items[i] ?? "", width - 2), style, width);
            r.Hits.Add(new Hit(new Rect(x, y + i, width, 1), HitKind.Select, id, i));
        }
        return items.Count;
    }

    static int DrawInput(Widget w, LayoutResult r, LayoutContext ctx, int x, int y, int width)
    {
        var id = w.Id ?? "input";
        string? typed = null;
        ctx.InputValues?.TryGetValue(id, out typed);
        var value = typed ?? w.Value ?? "";
        bool focused = ctx.FocusedInput == id;

        var field = new Style(Bg: Theme.InputBg);
        r.Canvas.Fill(x, y, width, 1, ' ', field);
        r.Canvas.Write(x, y, "› ", field with { Fg = focused ? Theme.Accent : Theme.Muted, Bold = true }, width);

        int available = width - 3; // prefix plus a cell for the cursor
        if (available > 0)
        {
            string shown = value.Length > available ? value[^available..] : value;
            if (shown.Length == 0 && !string.IsNullOrEmpty(w.Placeholder))
                r.Canvas.Write(x + 2, y, TextWrap.Truncate(w.Placeholder, available + 1), field with { Fg = Theme.Muted }, available + 1);
            else
                r.Canvas.Write(x + 2, y, shown, field, available);
            if (focused) r.Cursor = (x + 2 + shown.Length, y);
        }

        r.Hits.Add(new Hit(new Rect(x, y, width, 1), HitKind.Focus, id));
        return 1;
    }

    static int DrawDivider(LayoutResult r, int x, int y, int width)
    {
        r.Canvas.Fill(x, y, width, 1, '─', new Style(Fg: Theme.Border));
        return 1;
    }

    static int Color(string? hex, int fallback) => Style.Hex(hex) is var c && c != 0 ? c : fallback;
}
