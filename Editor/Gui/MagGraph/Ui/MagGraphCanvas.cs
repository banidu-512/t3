﻿#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Utils;
using T3.Editor.Gui.Graph;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.MagGraph.Interaction;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.Modification;
using T3.Editor.UiModel.ProjectSession;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.MagGraph.Ui;

/**
 * Draws and handles interaction with graph.
 */
internal sealed partial class MagGraphCanvas : ScalableCanvas
{
    public MagGraphCanvas(MagGraphWindow window, Instance newCompositionOp , NodeSelection nodeSelection, GraphImageBackground graphImageBackground)
    {
        EnableParentZoom = false;
        _window = window;
        _context = new GraphUiContext(nodeSelection, this, newCompositionOp , graphImageBackground);
        _nodeSelection = nodeSelection;
        
        InitializeCanvasScope(_context);
    }

    private ImRect _visibleCanvasArea;

    public bool IsRectVisible(ImRect rect)
    {
        return _visibleCanvasArea.Overlaps(rect);
    }

    public bool IsItemVisible(ISelectableCanvasObject item)
    {
        return IsRectVisible(ImRect.RectWithSize(item.PosOnCanvas, item.Size));
    }

    public bool IsFocused { get; private set; }
    public bool IsHovered { get; private set; }

    // private Guid _previousCompositionId;
    
    /// <summary>
    /// This is an intermediate helper method that should be replaced with a generalized implementation shared by
    /// all graph windows. It's especially unfortunate because it relies on GraphWindow.Focus to exist as open window :(
    ///
    /// It uses changes to context.CompositionOp to refresh the view to either the complete content or to the
    /// view saved in user settings...
    /// </summary>
    private void InitializeCanvasScope(GraphUiContext context)
    {
        // if (context.CompositionOp.SymbolChildId == _previousCompositionId)
        //     return;
        //
        if (ProjectEditing.FocusedCanvas == null)
            return;
        
        // _previousCompositionId = context.CompositionOp.SymbolChildId;
        
        // Meh: This relies on TargetScope already being set to new composition.
        var newCanvasScope = ProjectEditing.FocusedCanvas.GetTargetScope();
        if (UserSettings.Config.OperatorViewSettings.TryGetValue(context.CompositionOp.SymbolChildId, out var savedCanvasScope))
        {
            newCanvasScope = savedCanvasScope;
        }
        context.Canvas.SetScopeWithTransition(newCanvasScope.Scale, newCanvasScope.Scroll, ICanvas.Transition.Undefined);
    }
    
    
    public void Draw()
    {
        IsFocused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        IsHovered = ImGui.IsWindowHovered();

        // if (_window.WindowCompositionOp == null)
        //     return;
        //
        // if (_window.WindowCompositionOp != _context.CompositionOp)
        // {
        //     
        //     _context = new GraphUiContext(_nodeSelection, this, _window.WindowCompositionOp, _context.GraphImageBackground);
        // }

        _visibleCanvasArea = ImRect.RectWithSize(InverseTransformPositionFloat(ImGui.GetWindowPos()),
                                                 InverseTransformDirection(ImGui.GetWindowSize()));

        KeyboardActions.HandleKeyboardActions(_context);

        if (FitViewToSelectionHandling.FitViewToSelectionRequested)
            FocusViewToSelection(_context);

        _context.EditCommentDialog.Draw(_context.Selector);

        HandleSymbolDropping(_context);
        
        // Prepare frame
        //_context.Selector.HoveredIds.Clear();
        _context.Layout.ComputeLayout(_context);
        _context.ItemMovement.PrepareFrame();

        // Debug UI
        if (ImGui.Button("Center"))
            CenterView();

        ImGui.SameLine(0, 5);
        if (ImGui.Button("Rescan"))
            _context.Layout.ComputeLayout(_context, forceUpdate: true);

        ImGui.SameLine(0, 5);
        ImGui.Checkbox("Debug", ref _enableDebug);
        
        ImGui.SameLine(0, 10);
        ImGui.Text("" + GetTargetScope());

        UpdateCanvas(out _);
        var drawList = ImGui.GetWindowDrawList();

        if (_context.StateMachine.CurrentState == GraphStates.Default)
        {
            _context.ActiveItem = null;
            _context.ItemWithActiveCustomUi = null;
            _context.ActiveSourceOutputId = Guid.Empty;
        }

        DrawBackgroundGrids(drawList);

        // Selection fence...
        {
            HandleFenceSelection(_context, _selectionFence);
        }

        // Items
        foreach (var item in _context.Layout.Items.Values)
        {
            DrawItem(item, drawList, _context);
        }
        
        Fonts.FontSmall.Scale = 1;

        // Update hover time
        if (_context.ActiveItem != null)
        {
            if (_context.ActiveItem.Id != _lastHoverId)
            {
                _hoverStartTime = ImGui.GetTime();
                _lastHoverId = _context.ActiveItem.Id;
            }

            _context.Selector.HoveredIds.Add(_context.ActiveItem.Id);
        }
        else
        {
            _hoverStartTime = ImGui.GetTime(); 
            _lastHoverId = Guid.Empty;
        }

        HighlightSplitInsertionPoints(drawList, _context);

        // Connections
        foreach (var connection in _context.Layout.MagConnections)
        {
            DrawConnection(connection, drawList, _context);
        }

        // Draw temp connections
        foreach (var tc in _context.TempConnections)
        {
            var mousePos = ImGui.GetMousePos();
            
            var sourcePosOnScreen = mousePos;
            var targetPosOnScreen = mousePos;
            
            // Dragging end to new target input...
            if (tc.SourceItem != null)
            {
                //var outputLine = t.SourceItem.OutputLines[0];
                var sourcePos = new Vector2(tc.SourceItem.Area.Max.X,
                                            tc.SourceItem.Area.Min.Y + MagGraphItem.GridSize.Y * (0.5f + tc.OutputLineIndex));
                
                sourcePosOnScreen = TransformPosition(sourcePos);
                
                if (_context.StateMachine.CurrentState == GraphStates.DragConnectionEnd
                    && InputSnapper.BestInputMatch.Item != null)
                {
                    targetPosOnScreen = InputSnapper.BestInputMatch.PosOnScreen;
                }
            }

            // Dragging beginning to new source output...
            if (tc.TargetItem != null)
            {
                var targetPos = new Vector2(tc.TargetItem.Area.Min.X,
                                            tc.TargetItem.Area.Min.Y + MagGraphItem.GridSize.Y * (0.5f + tc.InputLineIndex));
                targetPosOnScreen = TransformPosition(targetPos);
                
                if (_context.StateMachine.CurrentState == GraphStates.DragConnectionBeginning
                    && OutputSnapper.BestOutputMatch.Item != null)
                {
                    sourcePosOnScreen = TransformPosition(OutputSnapper.BestOutputMatch.Anchor.PositionOnCanvas);
                }
                
                var isDisconnectedMultiInput = tc.InputLineIndex >= tc.TargetItem.InputLines.Length;
                if (isDisconnectedMultiInput)
                    continue;
            }
            else
            {
                if (_context.StateMachine.CurrentState == GraphStates.Placeholder)
                {
                    if (_context.Placeholder.PlaceholderItem != null)
                    {
                        targetPosOnScreen = TransformPosition(_context.Placeholder.PlaceholderItem.PosOnCanvas);
                    }
                }
                
            }


            var typeColor = TypeUiRegistry.GetPropertiesForType(tc.Type).Color;
            var d = Vector2.Distance(sourcePosOnScreen, targetPosOnScreen) / 2;

            drawList.AddBezierCubic(sourcePosOnScreen,
                                    sourcePosOnScreen + new Vector2(d, 0),
                                    targetPosOnScreen - new Vector2(d, 0),
                                    targetPosOnScreen,
                                    typeColor.Fade(0.6f),
                                    2);
        }
        
        OutputSnapper.Update(_context);
        InputSnapper.Update(_context);

        _context.ConnectionHovering.PrepareNewFrame(_context);

        _context.Placeholder.Update(_context);

        // Draw animated Snap indicator
        {
            var timeSinceSnap = ImGui.GetTime() - _context.ItemMovement.LastSnapTime;
            var progress = ((float)timeSinceSnap).RemapAndClamp(0, 0.4f, 1, 0);
            if (progress < 1)
            {
                drawList.AddCircle(TransformPosition(_context.ItemMovement.LastSnapTargetPositionOnCanvas),
                                   progress * 50,
                                   UiColors.ForegroundFull.Fade(progress * 0.2f));
            }
        }

        if (FrameStats.Current.OpenedPopUpName == string.Empty)
            CustomComponents.DrawContextMenuForScrollCanvas(() => GraphContextMenu.DrawContextMenuContent(_context), ref _contextMenuIsOpen);

        SmoothItemPositions();
        
        
        _context.StateMachine.UpdateAfterDraw(_context);
    }

    /// <summary>
    /// This a very simple proof-of-concept implementation to test it's fidelity.
    /// A simple optimization could be to only to this for some time after a drag manipulation and then apply
    /// the correct position. Also, this animation does not affect connection lines.
    ///
    /// It still helps to understand what's going on and feels satisfying. So we're keeping it for now.
    /// </summary>
    private void SmoothItemPositions()
    {
        foreach (var i in _context.Layout.Items.Values)
        {
            var dampAmount =  _context.ItemMovement.DraggedItems.Contains(i)
                                 ? 0.0f
                                 : 0.6f;
            i.DampedPosOnCanvas = Vector2.Lerp( i.PosOnCanvas, i.DampedPosOnCanvas,dampAmount);
        }
    }

    private bool _contextMenuIsOpen;

    private void HighlightSplitInsertionPoints(ImDrawListPtr drawList, GraphUiContext context)
    {
        foreach (var sp in context.ItemMovement.SplitInsertionPoints)
        {
            var inputItem = context.ItemMovement.DraggedItems.FirstOrDefault(i => i.Id == sp.InputItemId);
            if (inputItem == null)
                continue;

            var center = TransformPosition(inputItem.PosOnCanvas + sp.AnchorOffset);

            var offset = sp.Direction == MagGraphItem.Directions.Vertical 
                             ? new Vector2(MagGraphItem.GridSize.X / 16 * CanvasScale,0 ) 
                             : new Vector2(0, MagGraphItem.GridSize.Y / 8 * CanvasScale );

            {
                drawList.AddLine(center-offset, center+offset, 
                                 UiColors.ForegroundFull.Fade(MagGraphCanvas.Blink),
                                 2);
                //drawList.AddCircle(TransformPosition(inputItem.PosOnCanvas + sp.AnchorOffset), 3, UiColors.ForegroundFull.Fade(MagGraphCanvas.Blink));
            }
        }
    }
    
    private void HandleSymbolDropping(GraphUiContext context)
    {
        if (!DragHandling.IsDragging)
            return;

        ImGui.SetCursorPos(Vector2.Zero);
        ImGui.InvisibleButton("## drop", ImGui.GetWindowSize());

        if (!DragHandling.TryGetDataDroppedLastItem(DragHandling.SymbolDraggingId, out var data))
            return;
        
        if (!Guid.TryParse(data, out var guid))
        {
            Log.Warning("Invalid data format for drop? " + data);
            return;
        }

        if (SymbolUiRegistry.TryGetSymbolUi(guid, out var symbolUi))
        {
            var symbol = symbolUi.Symbol;
            var posOnCanvas = InverseTransformPositionFloat(ImGui.GetMousePos());
            if (!SymbolUiRegistry.TryGetSymbolUi(context.CompositionOp.Symbol.Id, out var compositionOpSymbolUi))
            {
                Log.Warning("Failed to get symbol id for " + context.CompositionOp.SymbolChildId);
                return;
            }
            
            var childUi = GraphOperations.AddSymbolChild(symbol, compositionOpSymbolUi, posOnCanvas);
            var instance = context.CompositionOp.Children[childUi.Id];
            context.Selector.SetSelection(childUi, instance);
            context.Layout.FlagAsChanged();
        }
        else
        {
            Log.Warning($"Symbol {guid} not found in registry");
        }
    }


    private void DrawBackgroundGrids(ImDrawListPtr drawList)
    {
        var minSize = MathF.Min(MagGraphItem.GridSize.X, MagGraphItem.GridSize.Y);
        var gridSize = Vector2.One * minSize;
        var maxOpacity = 0.25f;

        var fineGrid = MathUtils.RemapAndClamp(Scale.X, 0.5f, 2f, 0.0f, maxOpacity);
        if (fineGrid > 0.01f)
        {
            var color = UiColors.BackgroundFull.Fade(fineGrid);
            DrawBackgroundGrid(drawList, gridSize, color);
        }

        var roughGrid = MathUtils.RemapAndClamp(Scale.X, 0.1f, 2f, 0.0f, maxOpacity);
        if (roughGrid > 0.01f)
        {
            var color = UiColors.BackgroundFull.Fade(roughGrid);
            DrawBackgroundGrid(drawList, gridSize * 5, color);
        }
    }

    private void DrawBackgroundGrid(ImDrawListPtr drawList, Vector2 gridSize, Color color)
    {
        var window = new ImRect(ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize());

        var topLeftOnCanvas = InverseTransformPositionFloat(ImGui.GetWindowPos());
        var alignedTopLeftCanvas = new Vector2((int)(topLeftOnCanvas.X / gridSize.X) * gridSize.X,
                                               (int)(topLeftOnCanvas.Y / gridSize.Y) * gridSize.Y);

        var topLeftOnScreen = TransformPosition(alignedTopLeftCanvas);
        var screenGridSize = TransformDirection(gridSize);

        var count = new Vector2(window.GetWidth() / screenGridSize.X, window.GetHeight() / screenGridSize.Y);

        for (int ix = 0; ix < 200 && ix <= count.X + 1; ix++)
        {
            var x = (int)(topLeftOnScreen.X + ix * screenGridSize.X);
            drawList.AddRectFilled(new Vector2(x, window.Min.Y),
                                   new Vector2(x + 1, window.Max.Y),
                                   color);
        }

        for (int iy = 0; iy < 200 && iy <= count.Y + 1; iy++)
        {
            var y = (int)(topLeftOnScreen.Y + iy * screenGridSize.Y);
            drawList.AddRectFilled(new Vector2(window.Min.X, y),
                                   new Vector2(window.Max.X, y + 1),
                                   color);
        }
        
    }

    [Flags]
    private enum Borders
    {
        None = 0,
        Up = 1,
        Right = 2,
        Down = 4,
        Left = 8,
    }

    private static readonly ImDrawFlags[] _borderRoundings =
        {
            ImDrawFlags.RoundCornersAll, //        0000      
            ImDrawFlags.RoundCornersBottom, //     0001                 up
            ImDrawFlags.RoundCornersLeft, //       0010           right
            ImDrawFlags.RoundCornersBottomLeft, // 0011           right up
            ImDrawFlags.RoundCornersTop, //        0100      down
            ImDrawFlags.RoundCornersNone, //       0101      down       up
            ImDrawFlags.RoundCornersTopLeft, //    0110      down right  
            ImDrawFlags.RoundCornersNone, //       0111      down right up  

            ImDrawFlags.RoundCornersRight, //      1000 left
            ImDrawFlags.RoundCornersBottomRight, //1001 left            up
            ImDrawFlags.RoundCornersNone, //       1010 left      right
            ImDrawFlags.RoundCornersNone, //       1011 left      right up
            ImDrawFlags.RoundCornersTopRight, //   1100 left down
            ImDrawFlags.RoundCornersNone, //       1101 left down       up
            ImDrawFlags.RoundCornersNone, //       1110 left down right  
            ImDrawFlags.RoundCornersNone, //       1111 left down right up  
        };
    

    internal static float Blink => MathF.Sin((float)ImGui.GetTime() * 10) * 0.5f + 0.5f;

    private void HandleFenceSelection(GraphUiContext context, SelectionFence selectionFence)
    {
        var shouldBeActive =
                ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup)
                && (_context.StateMachine.CurrentState == GraphStates.Default 
                    || _context.StateMachine.CurrentState == GraphStates.HoldBackground)
                && _context.StateMachine.StateTime > 0.01f // Prevent glitches when coming from other states.
            ;

        if (!shouldBeActive)
        {
            selectionFence.Reset();
            return;
        }

        switch (selectionFence.UpdateAndDraw(out var selectMode))
        {
            case SelectionFence.States.PressedButNotMoved:
                if (selectMode == SelectionFence.SelectModes.Replace)
                    _context.Selector.Clear();
                break;

            case SelectionFence.States.Updated:
                HandleSelectionFenceUpdate(selectionFence.BoundsUnclamped, selectMode);
                break;

            case SelectionFence.States.CompletedAsClick:
                // A hack to prevent clearing selection when opening parameter popup
                if (ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopup))
                    break;

                _context.Selector.Clear();
                _context.Selector.SetSelectionToComposition(context.CompositionOp);
                break;
            case SelectionFence.States.Inactive:
                break;
            case SelectionFence.States.CompletedAsArea:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    // TODO: Support non graph items like annotations.
    private void HandleSelectionFenceUpdate(ImRect bounds, SelectionFence.SelectModes selectMode)
    {
        var boundsInCanvas = InverseTransformRect(bounds);
        var itemsInFence = (from child in _context.Layout.Items.Values
                            let rect = new ImRect(child.PosOnCanvas, child.PosOnCanvas + child.Size)
                            where rect.Overlaps(boundsInCanvas)
                            select child).ToList();

        if (selectMode == SelectionFence.SelectModes.Replace)
        {
            _context.Selector.Clear();
        }

        foreach (var item in itemsInFence)
        {
            if (selectMode == SelectionFence.SelectModes.Remove)
            {
                _context.Selector.DeselectNode(item, item.Instance);
            }
            else
            {
                if (item.Variant == MagGraphItem.Variants.Operator)
                {
                    _context.Selector.AddSelection(item.Selectable, item.Instance);
                }
                else
                {
                    _context.Selector.AddSelection(item.Selectable);
                }
            }
        }
    }

    private void CenterView()
    {
        var visibleArea = new ImRect();
        var isFirst = true;

        foreach (var item in _context.Layout.Items.Values)
        {
            if (isFirst)
            {
                visibleArea = item.Area;
                isFirst = false;
                continue;
            }

            visibleArea.Add(item.PosOnCanvas);
        }

        FitAreaOnCanvas(visibleArea);
    }

    private float GetHoverTimeForId(Guid id)
    {
        if (id != _lastHoverId)
            return 0;

        return HoverTime;
    }

    private readonly MagGraphWindow _window;

    private readonly SelectionFence _selectionFence = new();
    private Vector2 GridSizeOnScreen => TransformDirection(MagGraphItem.GridSize);
    private float CanvasScale => Scale.X;

    public bool ShowDebug => _enableDebug; // || ImGui.GetIO().KeyAlt;

    private Guid _lastHoverId;
    private double _hoverStartTime;
    private float HoverTime => (float)(ImGui.GetTime() - _hoverStartTime);
    private bool _enableDebug;
    private GraphUiContext _context;
    private readonly NodeSelection _nodeSelection;

    public override ScalableCanvas? Parent => null;

    public void FocusViewToSelection(GraphUiContext context)
    {
        FitAreaOnCanvas(NodeSelection.GetSelectionBounds(context.Selector, context.CompositionOp));
    }
}