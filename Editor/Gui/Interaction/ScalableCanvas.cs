using System.Runtime.CompilerServices;
using System.Text;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Utils;
//using T3.Editor.Gui.Graph.Legacy.Interaction.Connections;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.TimeLine;
using T3.Editor.UiModel.ProjectSession;

namespace T3.Editor.Gui.Interaction;

// hacky interface to extend IGraphCanvas
internal interface IScalableCanvas : ICanvas
{
    public void UpdateCanvas(out ScalableCanvas.InteractionState interactionState, T3Ui.EditingFlags flags = T3Ui.EditingFlags.None);
    public Vector2 ChildPosFromCanvas(Vector2 posOnCanvas);
    public void SetVisibleRange(Vector2 scale, Vector2 scroll);
    public void SetVisibleRangeHard(Vector2 scale, Vector2 scroll);
    public void SetScaleToMatchPixels();
    public void SetScopeWithTransition(Vector2 scale, Vector2 scroll, Transition transition);
    public void SetScopeToCanvasArea(ImRect area, bool flipY = false, IScalableCanvas parent = null, float paddingX = 0, float paddingY = 0);
    public void SetVerticalScopeToCanvasArea(ImRect area, bool flipY = false, ScalableCanvas parent = null);
    public void ZoomWithMouseWheel(MouseState mouseState, out bool zoomed);
    public Vector2 TransformPositionFloat(Vector2 posOnCanvas);
    public void FitAreaOnCanvas(ImRect areaOnCanvas, bool flipY = false);
    public void SetTargetScope(CanvasScope scope);
    public CanvasScope GetTargetScope();
    public bool EnableParentZoom { get; set; }
    public Vector2 ScaleTarget { get; }
}
/// <summary>
/// Implements transformations and interactions for a canvas that can
/// be zoomed and panned.
/// </summary>
internal abstract class ScalableCanvas : IScalableCanvas
{
    public ScalableCanvas(Vector2? initialScale = null)
    {
        if (initialScale == null)
            return;
        
        Scale = ScaleTarget = initialScale.Value;
    }
        
        
    /// <summary>
    /// This needs to be called by the inherited class before drawing its interface. 
    /// </summary>
    public void UpdateCanvas(out InteractionState interactionState, T3Ui.EditingFlags flags = T3Ui.EditingFlags.None)
    {
        var io = ImGui.GetIO();
        var mouse = new MouseState(io.MousePos, io.MouseDelta, io.MouseWheel);

        if (FillMode == FillModes.FillWindow)
        {
            WindowPos = ImGui.GetWindowContentRegionMin() + ImGui.GetWindowPos() + Vector2.One;
            WindowSize = ImGui.GetWindowContentRegionMax() - ImGui.GetWindowContentRegionMin() - 2 * Vector2.One;
        }
        else
        {
            WindowSize = ImGui.GetContentRegionAvail();
            WindowPos = ImGui.GetCursorScreenPos();
        }

        //if (!UsingParentCanvas)
        DampScaling(io.DeltaTime);

        HandleInteraction(flags, mouse, out var zoomed, out var panned);
        interactionState = new InteractionState(panned, zoomed, mouse);
    }
        
    #region implement ICanvas =================================================================
    /// <summary>
    /// Convert canvas position (e.g. of an Operator) into screen position  
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual Vector2 TransformPositionFloat(Vector2 posOnCanvas)
    {
        return (posOnCanvas - Scroll) * Scale * T3Ui.UiScaleFactor + WindowPos;
    }
        
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector2 TransformPosition(Vector2 posOnCanvas)
    {
        var v = TransformPositionFloat(posOnCanvas);
        return new Vector2((int)v.X, (int)v.Y);
    }
        
    /// <summary>
    /// Convert canvas position (e.g. of an Operator) to screen position  
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float TransformX(float xOnCanvas)
    {
        return TransformPosition(new Vector2(xOnCanvas, 0)).X;
    }
        
    /// <summary>
    ///  Convert canvas position (e.g. of an Operator) to screen position 
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float TransformY(float yOnCanvas)
    {
        return TransformPositionFloat(new Vector2(0, yOnCanvas)).Y;
    }
        
    /// <summary>
    /// Convert a screen space position (e.g. from mouse) to canvas coordinates  
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual Vector2 InverseTransformPositionFloat(Vector2 screenPos)
    {
        return (screenPos - WindowPos) / (Scale * T3Ui.UiScaleFactor) + Scroll;
    }
        
    /// <summary>
    /// Convert screen position to canvas position
    /// </summary>
    public virtual float InverseTransformX(float xOnScreen)
    {
        return InverseTransformPositionFloat(new Vector2(xOnScreen, 0)).X;
    }
        
    /// <summary>
    /// Convert screen position to canvas position
    /// </summary>

    public float InverseTransformY(float yOnScreen)
    {
        return InverseTransformPositionFloat(new Vector2(0, yOnScreen)).Y;
    }
        
    /// <summary>
    /// Convert a direction (e.g. MouseDelta) from ScreenSpace to Canvas
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector2 TransformDirection(Vector2 vectorInCanvas)
    {
        return TransformPositionFloat(vectorInCanvas) -
               TransformPositionFloat(new Vector2(0, 0));
    }
        
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector2 TransformDirectionFloored(Vector2 vectorInCanvas)
    {
        var s = TransformDirection(vectorInCanvas);
        return new Vector2((int)s.X, (int)s.Y);
    }
        
    /// <summary>
    /// Convert a direction (e.g. MouseDelta) from ScreenSpace to Canvas
    /// </summary>
    /// [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector2 InverseTransformDirection(Vector2 vectorInScreen)
    {
        return vectorInScreen / (Scale * T3Ui.UiScaleFactor);
    }
        
    public ImRect TransformRect(ImRect canvasRect)
    {
        // NOTE: We have to floor the size instead to min max position to avoid jittering  
        var min = TransformPositionFloat(canvasRect.Min);
        var max = TransformPositionFloat(canvasRect.Max);
        var size = max - min;
        min.X = (int)min.X;
        min.Y = (int)min.Y;
        size.X = (int)size.X;
        size.Y = (int)size.Y;
        return new ImRect(min, min + size);
    }
        
    public ImRect InverseTransformRect(ImRect screenRect)
    {
        return new ImRect(InverseTransformPositionFloat(screenRect.Min),
                          InverseTransformPositionFloat(screenRect.Max));
    }
        
    public virtual void UpdateScaleAndTranslation(Instance compositionOp, ICanvas.Transition transition)
    {
        // by default do nothing, overide in subclasses
    }
        
    /// <summary>
    /// Transform a canvas position to relative position within ImGui-window (e.g. to set ImGui context) 
    /// </summary>
    public Vector2 ChildPosFromCanvas(Vector2 posOnCanvas)
    {
        return TransformPositionFloat(posOnCanvas) - WindowPos;
        // (posOnCanvas - Scroll) * Scale;
    }
        
    public Vector2 WindowPos { get; private set; }
    public Vector2 WindowSize { get; private set; }
        
    public Vector2 Scale { get; protected set; } = Vector2.One;
    protected Vector2 ScaleTarget = Vector2.One;
    
    Vector2 IScalableCanvas.ScaleTarget => ScaleTarget;
        
    public Vector2 Scroll { get; protected set; } = new(0.0f, 0.0f);
    protected Vector2 ScrollTarget = new(0.0f, 0.0f);
    #endregion

    public CanvasScope GetTargetScope()
    {
        return new CanvasScope()
                   {
                       Scale = ScaleTarget,
                       Scroll = ScrollTarget
                   };
    }

    public void SetTargetScope(CanvasScope scope)
    {
        ScaleTarget = scope.Scale;
        ScrollTarget = scope.Scroll;
    }
        
    public void SetVisibleRange(Vector2 scale, Vector2 scroll)
    {
        ScaleTarget = scale;
        ScrollTarget = scroll;
    }
        
    public void SetVisibleRangeHard(Vector2 scale, Vector2 scroll)
    {
        Scale = ScaleTarget = scale;
        Scroll = ScrollTarget = scroll;
    }
        

    public void SetScaleToMatchPixels()
    {
        ScaleTarget = Vector2.One;
            
    }
        
    public void SetScopeToCanvasArea(ImRect area, bool flipY = false, IScalableCanvas parent = null, float paddingX = 0, float paddingY = 0)
    {
        var areaSize = area.GetSize();
        if (areaSize.X == 0)
            areaSize.X = 1;
            
        if (areaSize.Y == 0)
            areaSize.Y = 1;
            
        var newScale = (WindowSize - new Vector2(paddingX, paddingY));
        newScale.X = MathF.Max(newScale.X, 20);
        newScale.Y = MathF.Max(newScale.Y, 20);
            
        newScale /= areaSize;
            
        if (flipY)
        {
            newScale.Y *= -1;
        }
        ScrollTarget = new Vector2(area.Min.X - (paddingX / newScale.X) / 2,
                                   area.Max.Y - (paddingY / newScale.Y) / 2);
            
        // if (parent != null)
        // {
        //     newScale /= parent.Scale;
        // }
        ScaleTarget = newScale;
            
        if(ScaleTarget.X == 0 || ScaleTarget.Y == 0 || float.IsNaN(ScaleTarget.X) || float.IsNaN(ScaleTarget.Y) || float.IsInfinity(ScaleTarget.X) || float.IsInfinity(ScaleTarget.Y))
            Scale = ScaleTarget;
            
        if (float.IsNaN(ScrollTarget.X) || float.IsNaN(ScrollTarget.Y) || float.IsInfinity(ScrollTarget.X) || float.IsInfinity(ScrollTarget.Y))
            Scroll = ScrollTarget;
    }
        
    public void SetVerticalScopeToCanvasArea(ImRect area, bool flipY = false, ScalableCanvas parent = null)
    {
        WindowSize = ImGui.GetContentRegionMax() - ImGui.GetWindowContentRegionMin();
        ScaleTarget.Y = WindowSize.Y / area.GetSize().Y;
            
        if (flipY)
        {
            ScaleTarget.Y *= -1;
        }
            
        if (parent != null)
        {
            ScaleTarget.Y /= parent.Scale.Y;
        }
            
        ScrollTarget.Y = area.Max.Y;
    }
        
    public void FitAreaOnCanvas(ImRect areaOnCanvas, bool flipY = false)
    {
        var heightOnCanvas = areaOnCanvas.GetHeight();
        var widthOnCanvas = areaOnCanvas.GetWidth();
        var aspectOnCanvas = widthOnCanvas / heightOnCanvas;
            
        // Use a fallback resolution to fix initial call from constructor
        // where img has not been initialized yet.
        if (WindowSize == Vector2.Zero)
        {
            WindowSize = new Vector2(200, 200);
        }
            
        float scale;
        if (aspectOnCanvas > WindowSize.X / WindowSize.Y)
        {
            // Center in a high window...
            scale = WindowSize.X / widthOnCanvas;
            ScrollTarget = new Vector2(
                                       areaOnCanvas.Min.X,
                                       areaOnCanvas.Min.Y - (WindowSize.Y / scale - heightOnCanvas) / 2);
        }
        else
        {
            // Center in a wide window... 
            scale = WindowSize.Y / heightOnCanvas;
            ScrollTarget = new Vector2(
                                       areaOnCanvas.Min.X - (WindowSize.X / scale - widthOnCanvas) / 2,
                                       areaOnCanvas.Min.Y);
        }
            
        ScaleTarget = new Vector2(scale, scale);
        if (flipY)
        {
            ScaleTarget.Y *= -1;
        }
    }

    public void SetScopeWithTransition(Vector2 scale, Vector2 scroll, ICanvas.Transition transition)
    {
        if (float.IsInfinity(scale.X) || float.IsNaN(scale.X)
                                      || float.IsInfinity(scale.Y) || float.IsNaN(scale.Y)
                                      || float.IsInfinity(scroll.X) || float.IsNaN(scroll.X)
                                      || float.IsInfinity(scroll.Y) || float.IsNaN(scroll.Y)
           )
        {
            scale = Vector2.One;
            scroll = Vector2.Zero;
        }
            
        ScaleTarget = scale;
        ScrollTarget = scroll;
            
        switch (transition)
        {
            case ICanvas.Transition.JumpIn:
                Scale = ScaleTarget * 0.3f;
                var sizeOnCanvas = WindowSize / Scale;
                Scroll = ScrollTarget - sizeOnCanvas / 2;
                break;
                
            case ICanvas.Transition.JumpOut:
                Scale = ScaleTarget * 3f;
                var sizeOnCanvas2 = WindowSize / Scale;
                Scroll = ScrollTarget + sizeOnCanvas2 / 2;
                    
                break;
            default:
                Scroll = ScaleTarget;
                Scroll = ScrollTarget;
                break;
        }
    }

    private void DampScaling(float deltaTime)
    {
            
            
        var completed = Scale.X > 1000 || Math.Abs(Scroll.X - ScrollTarget.X) < 1f
                        && Math.Abs(Scroll.Y - ScrollTarget.Y) < 1f
                        && Math.Abs(Scale.X - ScaleTarget.X) < 0.05f
                        && Math.Abs(Scale.Y - ScaleTarget.Y) < 0.05f;
            
        if (completed)
        {
            Scroll = ScrollTarget;
            Scale = ScaleTarget;
            return;
        }
            
        // Damp scaling
        var minInCanvas = Scroll;
        var maxInCanvas = Scroll + WindowSize  / Scale;
        var minTargetInCanvas = ScrollTarget;
        var maxTargetInCanvas = ScrollTarget + WindowSize / ScaleTarget;

        var f = Math.Min(deltaTime / UserSettings.Config.ScrollSmoothing.Clamp(0.01f, 0.99f), 1);

        var min = Vector2.Lerp(minInCanvas, minTargetInCanvas, f);
        var max = Vector2.Lerp(maxInCanvas, maxTargetInCanvas, f);
        Scale = WindowSize  / (max - min);
        Scroll = min;
            


        if (float.IsNaN(ScaleTarget.X))
            ScaleTarget.X = 1;
            
        if (float.IsNaN(ScaleTarget.Y))
            ScaleTarget.Y = 1;
            
        if (float.IsNaN(Scale.X) || float.IsNaN(Scale.Y) || MathF.Sign(ScaleTarget.Y) != MathF.Sign(Scale.Y))
            Scale = ScaleTarget;
            
        if (float.IsNaN(ScrollTarget.X))
            ScrollTarget.X = 0;
            
        if (float.IsNaN(ScrollTarget.Y))
            ScrollTarget.Y = 0;
            
        if (float.IsNaN(Scroll.X) || float.IsNaN(Scroll.Y))
            Scroll = ScrollTarget;
    }

    private void HandleInteraction(T3Ui.EditingFlags flags, in MouseState mouseState, out bool zoomed, out bool panned)
    {
        zoomed = false;
        panned = false;

        if (_draggedCanvas == this && !ImGui.IsMouseDragging(ImGuiMouseButton.Right))
            _draggedCanvas = null;
            
        var currentGraphWindow = ProjectEditing.FocusedCanvas;
        bool isCurrentGraphCanvas = currentGraphWindow == this;
        bool isDraggingConnection = false;

        if (isCurrentGraphCanvas)
        {
            isDraggingConnection = currentGraphWindow.HasActiveInteraction;
            // var tempConnections = ConnectionMaker.GetTempConnectionsFor(currentGraphWindow);
            // isDraggingConnection = tempConnections.Count > 0 && ImGui.IsWindowFocused();
        }
            
        // This is a work around to allow the curve edit canvas to control zooming the timeline window
        var allowChildHover = flags.HasFlag(T3Ui.EditingFlags.AllowHoveredChildWindows)
                                  ? ImGuiHoveredFlags.ChildWindows
                                  : ImGuiHoveredFlags.None;

        var isWindowHovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup | allowChildHover);
        if (!_isDragZooming && !isWindowHovered && !isDraggingConnection)
            return;
            
        //DrawCanvasDebugInfos();

        if ((flags & T3Ui.EditingFlags.PreventMouseInteractions) != T3Ui.EditingFlags.None)
        {
            //Log.Debug($"Preventing {GetType().Name}");
            return;

        }

        //var isOtherWindowDragScrolling =  ImGui.GetID("");
        var isVerticalColorSliderActive = FrameStats.Last.OpenedPopUpName == "ColorBrightnessSlider";
        var isAnotherWindowDragged =  _draggedCanvas != null && _draggedCanvas != this 
                                      || CustomComponents.IsAnotherWindowDragScrolling(this);
        if (!isVerticalColorSliderActive
            && !isAnotherWindowDragged
            && !flags.HasFlag(T3Ui.EditingFlags.PreventPanningWithMouse)
            && ((
                        
                    ImGui.IsMouseDragging(ImGuiMouseButton.Left) && ImGui.GetIO().KeyAlt)
                || (!UserSettings.Config.MiddleMouseButtonZooms && ImGui.IsMouseDragging(ImGuiMouseButton.Middle) && !ImGui.GetIO().KeyAlt)
                || (ImGui.IsMouseDragging(ImGuiMouseButton.Right) && !ImGui.GetIO().KeyAlt))
           )
        {
            var parentScaleTarget = Parent?.ScaleTarget ?? Vector2.One;
            
            ScrollTarget -= mouseState.Delta / (parentScaleTarget * ScaleTarget);
            _draggedCanvas = this;
        }
            
        var preventZoom = flags.HasFlag(T3Ui.EditingFlags.PreventZoomWithMouseWheel);

        if (!preventZoom)
            //&& !ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopup))
        {
            ZoomWithMouseWheel(mouseState, out zoomed);
            ZoomWithDrag(ImGuiMouseButton.Right);
            ScaleTarget = ClampScaleToValidRange(ScaleTarget);
        }
            
        //Log.Debug($"({GetType().Name}) {nameof(preventPanning)}: {preventPanning}, {nameof(mouseIsDragging)}: {mouseIsDragging}, {nameof(preventZoom)}: {preventZoom}");
    }
        
    protected static ScalableCanvas _draggedCanvas;
    public static bool IsAnyCanvasDragged => _draggedCanvas != null;
        
    protected Vector2 ClampScaleToValidRange(Vector2 scale)
    {
        if (IsCurveCanvas)
            return scale;
            
        return this is TimeLineCanvas
                   ? new Vector2(scale.X.Clamp(0.01f, 5000), scale.Y.Clamp(0.01f, 5000))
                   : new Vector2(scale.X.Clamp(0.1f, 40), scale.Y.Clamp(0.1f, 40));
    }

    public void ZoomWithMouseWheel(MouseState mouseState, out bool zoomed)
    { 
        var zoomDelta = ComputeZoomDeltaFromMouseWheel(mouseState);
        ApplyZoomDelta(mouseState.Position, zoomDelta, out zoomed);
    }

    protected void ApplyZoomDelta(Vector2 position, float zoomDelta, out bool zoomed)
    {
        var clamped = ClampScaleToValidRange(ScaleTarget * zoomDelta);
        zoomed = false;
        if (clamped == ScaleTarget)
            return;
            
        if (Math.Abs(zoomDelta - 1) < 0.001f)
            return;
            
        var zoom = zoomDelta * Vector2.One;
        if (IsCurveCanvas)
        {
            if (ImGui.GetIO().KeyAlt)
            {
                zoom.X = 1;
            }
            else if (ImGui.GetIO().KeyShift)
            {
                zoom.Y = 1;
            }
        }
            
        ScaleTarget *= zoom;
            
        if (Math.Abs(zoomDelta) > 0.1f)
            zoomed = true;

        var focusCenterOnCanvas = InverseTransformPositionFloat(position);
        ScrollTarget += (focusCenterOnCanvas - ScrollTarget) * (zoom - Vector2.One) / zoom;
    }

    private void DrawCanvasDebugInfos(Vector2 mousePos)
    {
        var focusCenterOnCanvas = InverseTransformPositionFloat(mousePos);
        var dl = ImGui.GetForegroundDrawList();
            
        var focusOnScreen = TransformPosition(focusCenterOnCanvas);
        dl.AddCircle(focusOnScreen, 30, Color.Green);
        dl.AddText(focusOnScreen + new Vector2(0, 0), UiColors.StatusAnimated, $"{focusCenterOnCanvas.X:0.0} {focusCenterOnCanvas.Y:0.0} ");
            
        var wp = ImGui.GetWindowPos();
        dl.AddRectFilled(wp, wp + new Vector2(200, 100), UiColors.WindowBackground.Fade(0.4f));
        dl.AddText(wp + new Vector2(0, 0), UiColors.StatusAnimated, $"SCAL: {ScaleTarget.X:0.0} {ScaleTarget.Y:0.0} ");
        dl.AddText(wp + new Vector2(0, 16), UiColors.StatusAnimated, $"SCRL: {ScrollTarget.X:0.0} {ScrollTarget.Y:0.0} ");
        dl.AddText(wp + new Vector2(0, 32), UiColors.StatusAnimated, $"CNVS: {focusCenterOnCanvas.X:0.0} {focusCenterOnCanvas.Y:0.0} ");
        var hovered = ImGui.IsWindowHovered() ? "hovered" : "";
        var hoveredChild = ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows) ? "hoveredChildWindows" : "";
        dl.AddText(wp + new Vector2(0, 48), UiColors.StatusAnimated, $"{hovered} {hoveredChild}");
            
        var focused = ImGui.IsWindowFocused() ? "focused" : "";
        var focusedChild = ImGui.IsWindowFocused(ImGuiFocusedFlags.ChildWindows) ? "focusedChildWindows" : "";
        dl.AddText(wp + new Vector2(0, 64), UiColors.StatusAnimated, $"{focused} {focusedChild}");
    }
        
    protected bool IsCurveCanvas => Scale.Y < 0;

    protected float ComputeZoomDeltaFromMouseWheel(in MouseState mouseState)
    {
        var ioMouseWheel = mouseState.ScrollWheel;
        if (ioMouseWheel == 0)
            return 1;
            
        const float zoomSpeed = 1.2f;
        var zoomSum = 1f;
            
        if (ioMouseWheel < 0.0f)
        {
            for (var zoom = ioMouseWheel; zoom < 0.0f; zoom += 1.0f)
            {
                zoomSum /= zoomSpeed;
            }
        }
            
        if (ioMouseWheel > 0.0f)
        {
            for (var zoom = ioMouseWheel; zoom > 0.0f; zoom -= 1.0f)
            {
                zoomSum *= zoomSpeed;
            }
        }
            
        zoomSum = zoomSum.Clamp(0.02f, 100f);
        return zoomSum;
    }
        
    private Vector2 _mousePosWhenDragZoomStarted;
    private Vector2 _scaleWhenDragZoomStarted;
    private bool _isDragZooming;
    private float _lastZoomDelta;

    private void ZoomWithDrag(ImGuiMouseButton mouseButton )
    {
        mouseButton = UserSettings.Config.MiddleMouseButtonZooms
                          ? ImGuiMouseButton.Middle
                          : ImGuiMouseButton.Right;
            
        var hotkeysMatch = UserSettings.Config.MiddleMouseButtonZooms || ImGui.GetIO().KeyShift;
            
        if (ImGui.IsMouseClicked(mouseButton) && hotkeysMatch)
        {
            _isDragZooming = true;
            _lastZoomDelta = 1;
            _mousePosWhenDragZoomStarted = ImGui.GetMousePos();
            _scaleWhenDragZoomStarted = ScaleTarget;
        }
            
        if (ImGui.IsMouseReleased(mouseButton))
            _isDragZooming = false;
            
        if (!ImGui.IsMouseDragging(mouseButton, 0))
            return;
            
        var delta = ImGui.GetMousePos() - _mousePosWhenDragZoomStarted;
        var deltaMax = Math.Abs(delta.X) > Math.Abs(delta.Y)
                           ? -delta.X
                           : delta.Y;
            
        if (!_isDragZooming)
            return;
            
        var f = (float)Math.Pow(1.13f, -deltaMax / 40f);
        var delta2 =   f/_lastZoomDelta;
        ApplyZoomDelta(_mousePosWhenDragZoomStarted, delta2, out var zoomed); // FIXME: unclear what this does
        _lastZoomDelta = f;
    }


    public enum FillModes
    {
        FillWindow,
        FillAvailableContentRegion,
    }
        
    public FillModes FillMode = FillModes.FillWindow;
    
    public bool EnableParentZoom { get; set; } = true;

    public abstract IScalableCanvas? Parent { get; }
        
    public readonly record struct InteractionState(bool UserPannedCanvas, bool UserZoomedCanvas, MouseState MouseState);

    public static string PrintInteractionState(in InteractionState state)
    {
        var sb = new StringBuilder();
        sb.Append("Panned: ");
        sb.Append(state.UserPannedCanvas ? "Yes" : "No");
        sb.Append("  Zoomed: ");
        sb.Append(state.UserZoomedCanvas ? "Yes" : "No");
        sb.Append("  Mouse: ");
        sb.Append(state.MouseState.Position);
        var str = sb.ToString();
        Log.Debug(str);
        return str;
    }
}

public struct CanvasScope
{
    public Vector2 Scale;
    public Vector2 Scroll;
    public override string ToString()
    {
        return $"[{Scroll:0} ×{Scale:0.00}]";
    }
}

public readonly record struct MouseState(Vector2 Position, Vector2 Delta, float ScrollWheel);