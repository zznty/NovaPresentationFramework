using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Nova.Mil;
using Nova.Sdl;
using Nova.SdlSource;

namespace Nova.Framework.Tests;

// Headless ContextMenu proof: opens a real ContextMenu through the live Popup → PopupSecurityHelper
// → SdlPresentationSource path (offscreen SDL driver), verifies a second (popup) source exists and
// renders, invokes a MenuItem through the popup source's Dispatch with a pushed mouse event, and
// verifies the menu closes and the DuceRuntime bindings/channel mappings drain.
public sealed partial class WindowTextBlockTests
{
    [Fact]
    public void ToolTip_DefaultMousePlacement_OpensAndRendersWithoutUser32()
    {
        // Regression: a ToolTip with the DEFAULT PlacementMode.Mouse crashed on
        // Popup.GetMouseCursorSize → user32 GetCursor (DllNotFound). The Linux branch
        // (patch 0009) returns the documented SDL cursor default (32x32, hotspot 0,0),
        // and the mouse position arrives desktop-relative in device px via
        // SdlHost.GetGlobalMousePosition (last mouse event window-relative + window
        // origin — SDL_GetGlobalMouseState is unreliable on Wayland) — the same space
        // SDL positions popups — so no logical/device conversion is needed in the
        // mouse path.
        var button = new Button { Content = "Hover me", Width = 100, Height = 32 };
        var tip = new ToolTip { Content = "tooltip text" };
        button.ToolTip = tip;

        var window = new Window { Width = 320, Height = 200, Content = button };
        window.Show();
        try
        {
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
            // No explicit Placement is set: Mouse is the ToolTip default.
            Assert.Equal(System.Windows.Controls.Primitives.PlacementMode.Mouse, tip.Placement);

            tip.PlacementTarget = button;
            tip.IsOpen = true;
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Render);
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);

            Assert.True(tip.IsOpen);
            SdlPresentationSource popupSource = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(tip));
            var mainSource = (SdlPresentationSource)PresentationSource.FromVisual(window)!;
            Assert.True(ReferenceEquals(popupSource.Owner, mainSource), "tooltip popup must be owner-parented to the main window's source");

            // The tooltip popup must actually render (theme background + text) and be
            // positioned sanely in desktop-relative device px.
            popupSource.EnableReadback();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            ReadOnlyMemory<byte> pixels = popupSource.ReadbackRgba();
            int distinctColors = 0;
            var seen = new HashSet<int>();
            for (int i = 0; i + 3 < pixels.Length; i += 4)
            {
                int color = pixels.Span[i] | (pixels.Span[i + 1] << 8) | (pixels.Span[i + 2] << 16) | (pixels.Span[i + 3] << 24);
                if (seen.Add(color))
                {
                    distinctColors++;
                }
            }

            Assert.True(distinctColors >= 2, $"tooltip frame shows only {distinctColors} distinct color(s) — the tooltip did not render");
            Assert.True(popupSource.PixelWidth > 0 && popupSource.PixelHeight > 0, "tooltip popup should have non-zero size");
        }
        finally
        {
            tip.IsOpen = false;
            window.Close();
        }
    }

    [Fact]
    public void ContextMenu_Opens_Renders_InvokesItem_Closes_NoLeak()
    {
        var text = new TextBlock { Text = "Hi" };
        var button = new Button { Content = "Right-click me", Width = 120, Height = 32 };
        var menuItemCopy = new MenuItem { Header = "Copy label" };
        menuItemCopy.Click += (_, _) => text.Text = "menu: copy";
        var menuItemReset = new MenuItem { Header = "Reset label" };
        menuItemReset.Click += (_, _) => text.Text = "Hi";
        var menu = new ContextMenu();
        _ = menu.Items.Add(menuItemCopy);
        _ = menu.Items.Add(menuItemReset);
        button.ContextMenu = menu;
        // The right-click gesture sets PlacementTarget to the element; IsOpen alone does not.
        menu.PlacementTarget = button;

        var window = new Window { Width = 320, Height = 200, Content = button };
        window.Show();
        try
        {
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
            Assert.False(menu.IsOpen);

            // Open the menu the way WPF does on right-click: IsOpen = true after the
            // ContextMenuOpening event. This drives Popup.CreateWindow → BuildWindow →
            // PopupSecurityHelper.BuildWindow → new SdlPresentationSource(param, owner, tooltip).
            menu.IsOpen = true;
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Render);
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);

            Assert.True(menu.IsOpen);
            Assert.True(menuItemCopy.IsVisible, "menu item should be realized and visible once the popup is open");
            Assert.True(menuItemCopy.ActualWidth > 0 && menuItemCopy.ActualHeight > 0, "menu item should have non-zero layout size");

            // The popup is a second SdlPresentationSource; find it from the menu item's visual.
            SdlPresentationSource popupSource = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(menuItemCopy));
            var mainSource = (SdlPresentationSource)PresentationSource.FromVisual(window)!;
            Assert.True(ReferenceEquals(popupSource.Owner, mainSource), "popup must be owner-parented to the main window's source");
            Assert.NotEqual(popupSource.Handle, mainSource.Handle);

            // The popup must actually render: enable readback, present, and check for menu pixels
            // (distinct from the transparent/black clear — the menu has a theme background and text).
            popupSource.EnableReadback();
            popupSource.Present();
            popupSource.Present();
            ReadOnlyMemory<byte> pixels = popupSource.ReadbackRgba();
            int distinctColors = 0;
            byte first = pixels.Span[0];
            for (int i = 4; i < pixels.Length && distinctColors < 2; i += 4)
            {
                if (pixels.Span[i] != first || pixels.Span[i + 1] != pixels.Span[1] || pixels.Span[i + 2] != pixels.Span[2])
                {
                    distinctColors++;
                }
            }


            Assert.True(distinctColors >= 1, "popup readback must show more than a single flat clear color (menu rendered)");
            // Invoke the first item through the live input path: push a mouse Down+Up at the
            // item's rect through the popup source's Dispatch (client co-ods = popup-root co-ods).
            System.Windows.Point itemCenter = menuItemCopy.TranslatePoint(
                new System.Windows.Point(menuItemCopy.ActualWidth / 2, menuItemCopy.ActualHeight / 2),
                (UIElement)popupSource.RootVisual);
            var popupHandle = new WindowHandle(popupSource.Handle);
            var itemPoint = new Nova.Geometry.Point(itemCenter.X, itemCenter.Y);
            popupSource.Dispatch(new SdlEvent(
                SdlEventKind.MouseButtonDown,
                popupHandle,
                itemPoint,
                Nova.Geometry.Vector.Zero,
                Sdl.MouseButton.Left,
                0,
                null));
            popupSource.Dispatch(new SdlEvent(
                SdlEventKind.MouseButtonUp,
                popupHandle,
                itemPoint,
                Nova.Geometry.Vector.Zero,
                Sdl.MouseButton.Left,
                0,
                null));
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Render);
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);

            Assert.Equal("menu: copy", text.Text);
            Assert.False(menu.IsOpen, "invoking a menu item must close the menu");
            Assert.True(popupSource.IsDisposed, "the popup source must be disposed when the menu closes");
            // Only the main window's binding remains; the popup's binding drained on close.
            Assert.Equal(1, CountBindingsCtx());
            Assert.True(CountChannelMappingsCtx() >= 1, "the main window's channel mappings remain");

            // The main window must still be live and input-reachable after the menu closed.
            Assert.True(PresentationSource.FromVisual(window) is SdlPresentationSource { IsDisposed: false });
        }
        finally
        {
            if (menu.IsOpen)
            {
                menu.IsOpen = false;
            }

            window.Close();
        }
    }

    private static int CountBindingsCtx()
    {
        FieldInfo field = typeof(DuceRuntime).GetField("s_bindings", BindingFlags.Static | BindingFlags.NonPublic)!;
        return ((System.Collections.ICollection)field.GetValue(null)!).Count;
    }

    private static int CountChannelMappingsCtx()
    {
        FieldInfo field = typeof(DuceRuntime).GetField("s_graphsByChannel", BindingFlags.Static | BindingFlags.NonPublic)!;
        return ((System.Collections.ICollection)field.GetValue(null)!).Count;
    }
}
