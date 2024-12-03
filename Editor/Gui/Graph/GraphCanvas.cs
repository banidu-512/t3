using System.IO;
using System.Runtime.InteropServices;
using ImGuiNET;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.DataTypes;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Resource;
using T3.Core.SystemUi;
using T3.Core.UserData;
using T3.Editor.External;
using T3.Editor.Gui.Commands;
using T3.Editor.Gui.Commands.Annotations;
using T3.Editor.Gui.Commands.Graph;
using T3.Editor.Gui.Graph.Dialogs;
using T3.Editor.Gui.Graph.Helpers;
using T3.Editor.Gui.Graph.Interaction;
using T3.Editor.Gui.Graph.Interaction.Connections;
using T3.Editor.Gui.Graph.Modification;
using T3.Editor.Gui.InputUi;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.OutputUi;
using T3.Editor.Gui.Selection;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Output;
using T3.Editor.SystemUi;
using T3.Editor.UiModel;
using T3.SystemUi;
using ComputeShader = T3.Core.DataTypes.ComputeShader;
using GeometryShader = T3.Core.DataTypes.GeometryShader;
using PixelShader = T3.Core.DataTypes.PixelShader;
using Texture2D = SharpDX.Direct3D11.Texture2D;
using VertexShader = T3.Core.DataTypes.VertexShader;

namespace T3.Editor.Gui.Graph;

/// <summary>
/// A <see cref="ICanvas"/> that displays the graph of an Operator.
/// </summary>
internal class GraphCanvas : ScalableCanvas, INodeCanvas
{
    internal GraphCanvas(GraphWindow window, NodeSelection nodeSelection, NavigationHistory navigationHistory)
    {
        _window = window;
        Structure = window.Structure;
        NavigationHistory = navigationHistory;
        NodeSelection = nodeSelection;
        _nodeGraphLayouting = new NodeGraphLayouting(NodeSelection, Structure);
        NodeNavigation = new NodeNavigation(() => window.CompositionOp, this, Structure, NavigationHistory);
        SelectableNodeMovement = new SelectableNodeMovement(window, this, NodeSelection);
        _graph = new Graph(window, this);

        window.WindowDestroyed += (_, _) => Destroyed = true;
        window.FocusLost += (_, _) =>
                            {
                                NodeSelection.Clear();
                                NodeSelection.HoveredIds.Clear();
                            };
    }

    public void SetCompositionToChildInstance(Instance instance) => _window.TrySetCompositionOpToChild(instance.SymbolChildId);

    [Flags]
    public enum GraphDrawingFlags
    {
        None = 0,
        PreventInteractions = 1 << 1,
        HideGrid = 1 << 2,
    }

    #region drawing UI ====================================================================
    /// <summary>
    /// Scales the canvas and handles basic canvas interactions
    /// </summary>
    public void Draw(bool preventInteractions)
    {
        var editingFlags = T3Ui.EditingFlags.None;

        if (_window.SymbolBrowser.IsOpen)
            editingFlags |= T3Ui.EditingFlags.PreventZoomWithMouseWheel;

        if (preventInteractions)
            editingFlags |= T3Ui.EditingFlags.PreventMouseInteractions;

        UpdateCanvas(out _, editingFlags);
    }

    public void DrawGraph(ImDrawListPtr drawList, GraphDrawingFlags drawingFlags, bool preventInteractions, float graphOpacity)
    {
        ImGui.BeginGroup();
        {
            ImGui.SetScrollY(0);
            CustomComponents.DrawWindowFocusFrame();
            if (ImGui.IsWindowFocused())
                _window.TakeFocus();

            DrawDropHandler(_window.CompositionOp, _window.CompositionOp.GetSymbolUi());
            ImGui.SetCursorScreenPos(Vector2.One * 100);

            if (!preventInteractions)
            {
                var compositionOp = _window.CompositionOp;
                var compositionUi = compositionOp.GetSymbolUi();
                //compositionUi.FlagAsModified();

                if (KeyboardBinding.Triggered(UserActions.FocusSelection))
                    FocusViewToSelection();

                if (!T3Ui.IsCurrentlySaving && KeyboardBinding.Triggered(UserActions.Duplicate))
                {
                    NodeActions.CopySelectedNodesToClipboard(NodeSelection, compositionOp);
                    NodeActions.PasteClipboard(NodeSelection, this, compositionOp);
                }

                if (!T3Ui.IsCurrentlySaving && KeyboardBinding.Triggered(UserActions.DeleteSelection))
                {
                    NodeActions.DeleteSelectedElements(NodeSelection, compositionUi);
                }

                if (KeyboardBinding.Triggered(UserActions.ToggleDisabled))
                {
                    NodeActions.ToggleDisabledForSelectedElements(NodeSelection);
                }

                if (KeyboardBinding.Triggered(UserActions.ToggleBypassed))
                {
                    NodeActions.ToggleBypassedForSelectedElements(NodeSelection);
                }

                if (KeyboardBinding.Triggered(UserActions.PinToOutputWindow))
                {
                    if (UserSettings.Config.FocusMode)
                    {
                        var selectedImage = NodeSelection.GetFirstSelectedInstance();
                        if (selectedImage != null && GraphWindow.Focused != null)
                        {
                            GraphWindow.Focused.SetBackgroundOutput(selectedImage);
                        }
                    }
                    else
                    {
                        NodeActions.PinSelectedToOutputWindow(this, NodeSelection, compositionOp);
                    }
                }

                if (KeyboardBinding.Triggered(UserActions.DisplayImageAsBackground))
                {
                    var selectedImage = NodeSelection.GetFirstSelectedInstance();
                    if (selectedImage != null && GraphWindow.Focused != null)
                    {
                        GraphWindow.Focused.SetBackgroundOutput(selectedImage);
                        //GraphWindow.Focused..SetBackgroundInstanceForCurrentGraph(selectedImage);
                    }
                }

                if (KeyboardBinding.Triggered(UserActions.CopyToClipboard))
                {
                    NodeActions.CopySelectedNodesToClipboard(NodeSelection, compositionOp);
                }

                if (!T3Ui.IsCurrentlySaving && KeyboardBinding.Triggered(UserActions.PasteFromClipboard))
                {
                    NodeActions.PasteClipboard(NodeSelection, this, compositionOp);
                }

                if (KeyboardBinding.Triggered(UserActions.LayoutSelection))
                {
                    _nodeGraphLayouting.ArrangeOps(compositionOp);
                }

                if (!T3Ui.IsCurrentlySaving && KeyboardBinding.Triggered(UserActions.AddAnnotation))
                {
                    var newAnnotation = NodeActions.AddAnnotation(NodeSelection, this, compositionOp);
                    _graph.RenameAnnotation(newAnnotation);
                }

                IReadOnlyList<Guid>? navigationPath = null;

                // Navigation
                if (KeyboardBinding.Triggered(UserActions.NavigateBackwards))
                {
                    navigationPath = NavigationHistory.NavigateBackwards();
                }

                if (KeyboardBinding.Triggered(UserActions.NavigateForward))
                {
                    navigationPath = NavigationHistory.NavigateForward();
                }

                if (navigationPath != null)
                    _window.TrySetCompositionOp(navigationPath);

                if (KeyboardBinding.Triggered(UserActions.SelectToAbove))
                {
                    NodeNavigation.SelectAbove();
                }

                if (KeyboardBinding.Triggered(UserActions.SelectToRight))
                {
                    NodeNavigation.SelectRight();
                }

                if (KeyboardBinding.Triggered(UserActions.SelectToBelow))
                {
                    NodeNavigation.SelectBelow();
                }

                if (KeyboardBinding.Triggered(UserActions.AddComment))
                {
                    EditCommentDialog.ShowNextFrame();
                }

                if (KeyboardBinding.Triggered(UserActions.SelectToLeft))
                {
                    NodeNavigation.SelectLeft();
                }

                if (KeyboardBinding.Triggered(UserActions.DisplayImageAsBackground))
                {
                    var selectedImage = NodeSelection.GetFirstSelectedInstance();
                    if (selectedImage != null)
                    {
                        _window.GraphImageBackground.OutputInstance = selectedImage;
                    }
                }
            }

            if (ImGui.IsWindowFocused() && !preventInteractions)
            {
                var io = ImGui.GetIO();
                var editingSomething = ImGui.IsAnyItemActive();

                if (!io.KeyCtrl && !io.KeyShift && !io.KeyAlt && !editingSomething)
                {
                    if (ImGui.IsKeyDown((ImGuiKey)Key.W))
                    {
                        _dampedScrollVelocity.Y -= InverseTransformDirection(Vector2.One * UserSettings.Config.KeyboardScrollAcceleration).Y;
                    }

                    if (ImGui.IsKeyDown((ImGuiKey)Key.S))
                    {
                        _dampedScrollVelocity.Y += InverseTransformDirection(Vector2.One * UserSettings.Config.KeyboardScrollAcceleration).Y;
                    }

                    if (ImGui.IsKeyDown((ImGuiKey)Key.A))
                    {
                        _dampedScrollVelocity.X -= InverseTransformDirection(Vector2.One * UserSettings.Config.KeyboardScrollAcceleration).X;
                    }

                    if (ImGui.IsKeyDown((ImGuiKey)Key.D))
                    {
                        _dampedScrollVelocity.X += InverseTransformDirection(Vector2.One * UserSettings.Config.KeyboardScrollAcceleration).X;
                    }

                    if (ImGui.IsKeyDown((ImGuiKey)Key.Q))
                    {
                        var center = WindowPos + WindowSize / 2;
                        ApplyZoomDelta(center, 1.05f, out _);
                    }

                    if (ImGui.IsKeyDown((ImGuiKey)Key.E))
                    {
                        var center = WindowPos + WindowSize / 2;
                        ApplyZoomDelta(center, 1 / 1.05f, out _);
                    }
                }
            }

            ScrollTarget += _dampedScrollVelocity;
            _dampedScrollVelocity *= 0.90f;

            drawList.PushClipRect(WindowPos, WindowPos + WindowSize);

            if (!drawingFlags.HasFlag(GraphDrawingFlags.HideGrid))
                DrawGrid(drawList);

            if (ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
            {
                ConnectionSplitHelper.PrepareNewFrame(_window);
            }

            _window.SymbolBrowser.Draw();

            graphOpacity *= preventInteractions ? 0.3f : 1;
            _graph.DrawGraph(drawList, drawingFlags.HasFlag(GraphDrawingFlags.PreventInteractions), _window.CompositionOp, graphOpacity);

            RenameInstanceOverlay.Draw(_window);
            var tempConnections = ConnectionMaker.GetTempConnectionsFor(_window);

            var doubleClicked = ImGui.IsMouseDoubleClicked(0);

            var isSomething = (ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup) || ImGui.IsWindowFocused())
                              && !preventInteractions
                              && tempConnections.Count == 0;

            var isOnBackground = ImGui.IsWindowFocused() && !ImGui.IsAnyItemActive();
            var shouldHandleFenceSelection = isSomething
                                             || isOnBackground && (ImGui.IsMouseDoubleClicked(0) || KeyboardBinding.Triggered(UserActions.CloseOperator));

            if (shouldHandleFenceSelection)
            {
                HandleFenceSelection(_window.CompositionOp, _selectionFence);
            }

            if (isOnBackground && doubleClicked)
            {
                _window.TrySetCompositionOpToParent();
            }

            if (tempConnections.Count > 0 && ImGui.IsMouseReleased(0))
            {
                var isAnyItemHovered = ImGui.IsAnyItemHovered();
                var droppedOnBackground =
                    ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem | ImGuiHoveredFlags.AllowWhenBlockedByPopup) && !isAnyItemHovered;
                if (droppedOnBackground)
                {
                    ConnectionMaker.InitSymbolBrowserAtPosition(_window,
                                                                InverseTransformPositionFloat(ImGui.GetIO().MousePos));
                }
                else
                {
                    var connectionDroppedOnBackground =
                        tempConnections[0].GetStatus() != ConnectionMaker.TempConnection.Status.TargetIsDraftNode;
                    if (connectionDroppedOnBackground)
                    {
                        //Log.Warning("Skipping complete operation on background drop?");
                        //  ConnectionMaker.CompleteOperation();
                    }
                }
            }

            drawList.PopClipRect();

            var compositionUpdated = _window.CompositionOp;

            if (FrameStats.Current.OpenedPopUpName == string.Empty)
                CustomComponents.DrawContextMenuForScrollCanvas(() => DrawContextMenuContent(compositionUpdated), ref _contextMenuIsOpen);

            _duplicateSymbolDialog.Draw(compositionUpdated, NodeSelection.GetSelectedChildUis().ToList(), ref _nameSpaceForDialogEdits,
                                        ref _symbolNameForDialogEdits,
                                        ref _symbolDescriptionForDialog);
            _combineToSymbolDialog.Draw(compositionUpdated, NodeSelection.GetSelectedChildUis().ToList(),
                                        NodeSelection.GetSelectedNodes<Annotation>().ToList(),
                                        ref _nameSpaceForDialogEdits,
                                        ref _symbolNameForDialogEdits,
                                        ref _symbolDescriptionForDialog);

            _renameSymbolDialog.Draw(NodeSelection.GetSelectedChildUis().ToList(), ref _symbolNameForDialogEdits);

            EditCommentDialog.Draw(NodeSelection);

            if (compositionUpdated != _window.RootInstance.Instance && !compositionUpdated.Symbol.SymbolPackage.IsReadOnly)
            {
                var symbol = compositionUpdated.Symbol;
                _addInputDialog.Draw(symbol);
                _addOutputDialog.Draw(symbol);
            }

            LibWarningDialog.Draw(this);
            EditNodeOutputDialog.Draw();
        }
        ImGui.EndGroup();
    }

    private void HandleFenceSelection(Instance compositionOp, SelectionFence selectionFence)
    {
        const bool allowRectOutOfBounds = true;
        switch (selectionFence.UpdateAndDraw(out var selectMode, allowRectOutOfBounds))
        {
            case SelectionFence.States.PressedButNotMoved:
                if (selectMode == SelectionFence.SelectModes.Replace)
                    NodeSelection.Clear();
                break;

            case SelectionFence.States.Updated:
                var bounds = allowRectOutOfBounds ? selectionFence.BoundsUnclamped : selectionFence.BoundsInScreen;
                HandleSelectionFenceUpdate(bounds, compositionOp, selectMode);
                break;

            case SelectionFence.States.CompletedAsClick:
                // A hack to prevent clearing selection when opening parameter popup
                if (ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopup))
                    break;

                NodeSelection.Clear();
                NodeSelection.SetSelectionToComposition(compositionOp);
                break;
        }
    }

    private void HandleSelectionFenceUpdate(ImRect bounds, Instance compositionOp, SelectionFence.SelectModes selectMode)
    {
        var boundsInCanvas = InverseTransformRect(bounds);
        var nodesToSelect = NodeSelection.GetSelectableChildren(_window.CompositionOp)
                                         .Where(child => child is Annotation
                                                             ? boundsInCanvas.Contains(child.Rect)
                                                             : child.Rect.Overlaps(boundsInCanvas));

        if (selectMode == SelectionFence.SelectModes.Replace)
        {
            NodeSelection.Clear();
        }

        var isRemoval = selectMode == SelectionFence.SelectModes.Remove;

        foreach (var node in nodesToSelect)
        {
            if (node is SymbolUi.Child symbolChildUi)
            {
                if (!compositionOp.TryGetChildInstance(symbolChildUi.Id, false, out var instance, out _))
                {
                    Log.Error("Can't find instance");
                }

                if (isRemoval)
                {
                    NodeSelection.DeselectNode(symbolChildUi, instance);
                }
                else
                {
                    NodeSelection.AddSelection(symbolChildUi, instance);
                }
            }
            else
            {
                if (isRemoval)
                {
                    NodeSelection.DeselectNode(node);
                }
                else
                {
                    NodeSelection.AddSelection(node);
                }
            }
        }
    }

    /// <remarks>
    /// This method is complex, because it has to handle several edge cases and has potential to remove previous user data:
    /// - We have to preserve the previous state.
    /// - We have to make space -> Shift all connected operators towards the right.
    /// - We have to convert all existing connections from the output into temporary connections.
    /// - We have to insert a new temp connection line between output and symbol browser
    ///
    /// - If the user completes the symbol browser, it must complete the previous connections from the temp connections.
    /// - If the user cancels the operation, the previous state has to be restored. This might be tricky
    /// </remarks>
    public void OpenSymbolBrowserForOutput(SymbolUi.Child childUi, Symbol.OutputDefinition outputDef)
    {
        ConnectionMaker.InitSymbolBrowserAtPosition(_window,
                                                    childUi.PosOnCanvas + new Vector2(childUi.Size.X + SelectableNodeMovement.SnapPadding.X, 0));
    }

    // private Symbol GetSelectedSymbol()
    // {
    //     var selectedChildUi = GetSelectedChildUis().FirstOrDefault();
    //     return selectedChildUi != null ? selectedChildUi.SymbolChild.Symbol : compositionOp.Symbol;
    // }

    private void DrawDropHandler(Instance compositionOp, SymbolUi compositionOpSymbolUi)
    {
        if (!T3Ui.DraggingIsInProgress)
            return;

        ImGui.SetCursorPos(Vector2.Zero);
        ImGui.InvisibleButton("## drop", ImGui.GetWindowSize());

        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload("Symbol");
            if (ImGui.IsMouseReleased(0))
            {
                var myString = Marshal.PtrToStringAuto(payload.Data);
                if (myString != null)
                {
                    var guidString = myString.Split('|')[0];
                    var guid = Guid.Parse(guidString);
                    Log.Debug("dropped symbol here" + payload + " " + myString + "  " + guid);

                    if (SymbolUiRegistry.TryGetSymbolUi(guid, out var symbolUi))
                    {
                        var symbol = symbolUi.Symbol;
                        var posOnCanvas = InverseTransformPositionFloat(ImGui.GetMousePos());
                        var childUi = GraphOperations.AddSymbolChild(symbol, compositionOpSymbolUi, posOnCanvas);

                        var instance = compositionOp.Children[childUi.Id];
                        NodeSelection.SetSelection(childUi, instance);
                    }
                    else
                    {
                        Log.Warning($"Symbol {guid} not found in registry");
                    }

                    T3Ui.DraggingIsInProgress = false;
                }
            }

            ImGui.EndDragDropTarget();
        }
    }

    internal void FocusViewToSelection()
    {
        FitAreaOnCanvas(NodeSelection.GetSelectionBounds(NodeSelection, _window.CompositionOp));
    }

    private void DrawContextMenuContent(Instance compositionOp)
    {
        var clickPosition = ImGui.GetMousePosOnOpeningCurrentPopup();
        var compositionSymbolUi = compositionOp.GetSymbolUi();

        var selectedChildUis = NodeSelection.GetSelectedChildUis().ToList();
        var nextUndoTitle = UndoRedoStack.CanUndo ? $" ({UndoRedoStack.GetNextUndoTitle()})" : string.Empty;
        if (ImGui.MenuItem("Undo" + nextUndoTitle,
                           shortcut: KeyboardBinding.ListKeyboardShortcuts(UserActions.Undo, false),
                           selected: false,
                           enabled: UndoRedoStack.CanUndo))
        {
            UndoRedoStack.Undo();
        }

        ImGui.Separator();

        // ------ for selection -----------------------
        var oneOpSelected = selectedChildUis.Count == 1;
        var someOpsSelected = selectedChildUis.Count > 0;
        var snapShotsEnabledFromSomeOps
            = selectedChildUis
               .Any(selectedChildUi => selectedChildUi.EnabledForSnapshots);

        var label = oneOpSelected
                        ? $"{selectedChildUis[0].SymbolChild.ReadableName}..."
                        : $"{selectedChildUis.Count} selected items...";

        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Gray.Rgba);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();
        ImGui.PopFont();

        var allSelectedDisabled = selectedChildUis.TrueForAll(selectedChildUi => selectedChildUi.IsDisabled);
        if (ImGui.MenuItem("Disable",
                           KeyboardBinding.ListKeyboardShortcuts(UserActions.ToggleDisabled, false),
                           selected: allSelectedDisabled,
                           enabled: selectedChildUis.Count > 0))
        {
            NodeActions.ToggleDisabledForSelectedElements(NodeSelection);
        }

        var allSelectedBypassed = selectedChildUis.TrueForAll(selectedChildUi => selectedChildUi.SymbolChild.IsBypassed);
        if (ImGui.MenuItem("Bypassed",
                           KeyboardBinding.ListKeyboardShortcuts(UserActions.ToggleBypassed, false),
                           selected: allSelectedBypassed,
                           enabled: selectedChildUis.Count > 0))
        {
            NodeActions.ToggleBypassedForSelectedElements(NodeSelection);
        }

        if (ImGui.MenuItem("Rename", oneOpSelected))
        {
            RenameInstanceOverlay.OpenForChildUi(selectedChildUis[0]);
        }

        if (ImGui.MenuItem("Add Comment",
                           KeyboardBinding.ListKeyboardShortcuts(UserActions.AddComment, false),
                           selected: false,
                           enabled: oneOpSelected))
        {
            EditCommentDialog.ShowNextFrame();
        }

        if (ImGui.MenuItem("Arrange sub graph",
                           KeyboardBinding.ListKeyboardShortcuts(UserActions.LayoutSelection, false),
                           selected: false,
                           enabled: someOpsSelected))
        {
            _nodeGraphLayouting.ArrangeOps(compositionOp);
        }

        var canModify = !compositionSymbolUi.Symbol.SymbolPackage.IsReadOnly;
        if (canModify)
        {
            // Disable if already enabled for all
            var disableBecauseAllEnabled
                = selectedChildUis
                   .TrueForAll(c2 => c2.EnabledForSnapshots);

            foreach (var c in selectedChildUis)
            {
                c.EnabledForSnapshots = !disableBecauseAllEnabled;
            }

            // Add to add snapshots
            var allSnapshots = VariationHandling.ActivePoolForSnapshots?.AllVariations;
            if (allSnapshots != null && allSnapshots.Count > 0)
            {
                if (disableBecauseAllEnabled)
                {
                    VariationHandling.RemoveInstancesFromVariations(selectedChildUis.Select(ui => ui.Id), allSnapshots);
                }
                // Remove from snapshots
                else
                {
                    var selectedInstances = selectedChildUis
                                           .Select(ui => compositionOp.Children[ui.Id])
                                           .ToList();
                    foreach (var snapshot in allSnapshots)
                    {
                        VariationHandling.ActivePoolForSnapshots.UpdateVariationPropertiesForInstances(snapshot, selectedInstances);
                    }
                }
            }

            compositionSymbolUi.FlagAsModified();
        }

        if (ImGui.BeginMenu("Display as..."))
        {
            if (ImGui.MenuItem("Small", "",
                               selected: selectedChildUis.Any(child => child.Style == SymbolUi.Child.Styles.Default),
                               enabled: someOpsSelected))
            {
                foreach (var childUi in selectedChildUis)
                {
                    childUi.Style = SymbolUi.Child.Styles.Default;
                }
            }

            if (ImGui.MenuItem("Resizable", "",
                               selected: selectedChildUis.Any(child => child.Style == SymbolUi.Child.Styles.Resizable),
                               enabled: someOpsSelected))
            {
                foreach (var childUi in selectedChildUis)
                {
                    childUi.Style = SymbolUi.Child.Styles.Resizable;
                }
            }

            if (ImGui.MenuItem("Expanded", "",
                               selected: selectedChildUis.Any(child => child.Style == SymbolUi.Child.Styles.Resizable),
                               enabled: someOpsSelected))
            {
                foreach (var childUi in selectedChildUis)
                {
                    childUi.Style = SymbolUi.Child.Styles.Expanded;
                }
            }

            ImGui.Separator();

            var isImage = oneOpSelected
                          && selectedChildUis[0].SymbolChild.Symbol.OutputDefinitions.Count > 0
                          && selectedChildUis[0].SymbolChild.Symbol.OutputDefinitions[0].ValueType == typeof(Texture2D);
            if (ImGui.MenuItem("Set image as graph background",
                               KeyboardBinding.ListKeyboardShortcuts(UserActions.DisplayImageAsBackground, false),
                               selected: false,
                               enabled: isImage))
            {
                var instance = compositionOp.Children[selectedChildUis[0].Id];
                _window.GraphImageBackground.OutputInstance = instance;
            }

            if (ImGui.MenuItem("Pin to output", oneOpSelected))
            {
                NodeActions.PinSelectedToOutputWindow(this, NodeSelection, compositionOp);
            }

            ImGui.EndMenu();
        }

        ImGui.Separator();

        if (ImGui.MenuItem("Copy",
                           KeyboardBinding.ListKeyboardShortcuts(UserActions.CopyToClipboard, false),
                           selected: false,
                           enabled: someOpsSelected))
        {
            NodeActions.CopySelectedNodesToClipboard(NodeSelection, compositionOp);
        }

        if (ImGui.MenuItem("Paste", KeyboardBinding.ListKeyboardShortcuts(UserActions.PasteFromClipboard, false)))
        {
            NodeActions.PasteClipboard(NodeSelection, this, compositionOp);
        }

        var selectedInputUis = NodeSelection.GetSelectedNodes<IInputUi>().ToList();
        var selectedOutputUis = NodeSelection.GetSelectedNodes<IOutputUi>().ToList();

        var isSaving = T3Ui.IsCurrentlySaving;

        if (ImGui.MenuItem("Delete",
                           shortcut: "Del", // dynamic assigned shortcut is too long
                           selected: false,
                           enabled: (someOpsSelected || selectedInputUis.Count > 0 || selectedOutputUis.Count > 0) && !isSaving))
        {
            NodeActions.DeleteSelectedElements(NodeSelection, compositionSymbolUi, selectedChildUis, selectedInputUis, selectedOutputUis);
        }

        if (ImGui.MenuItem("Duplicate",
                           KeyboardBinding.ListKeyboardShortcuts(UserActions.Duplicate, false),
                           selected: false,
                           enabled: selectedChildUis.Count > 0 && !isSaving))
        {
            NodeActions.CopySelectedNodesToClipboard(NodeSelection, compositionOp);
            NodeActions.PasteClipboard(NodeSelection, this, compositionOp);
        }

        ImGui.Separator();

        if (ImGui.MenuItem("Change Symbol", someOpsSelected && !isSaving))
        {
            var startingSearchString = selectedChildUis[0].SymbolChild.Symbol.Name;
            var position = selectedChildUis.Count == 1 ? selectedChildUis[0].PosOnCanvas : InverseTransformPositionFloat(ImGui.GetMousePos());
            _window.SymbolBrowser.OpenAt(position, null, null, false, startingSearchString,
                                         symbol => { ChangeSymbol.ChangeOperatorSymbol(NodeSelection, compositionOp, selectedChildUis, symbol); });
        }

        if (ImGui.BeginMenu("Symbol definition...", !isSaving))
        {
            if (ImGui.MenuItem("Rename Symbol", oneOpSelected))
            {
                _renameSymbolDialog.ShowNextFrame();
                _symbolNameForDialogEdits = selectedChildUis[0].SymbolChild.Symbol.Name;
                //NodeOperations.RenameSymbol(selectedChildUis[0].SymbolChild.Symbol, "NewName");
            }

            if (ImGui.MenuItem("Duplicate as new type...", oneOpSelected))
            {
                _symbolNameForDialogEdits = selectedChildUis[0].SymbolChild.Symbol.Name ?? string.Empty;
                _nameSpaceForDialogEdits = selectedChildUis[0].SymbolChild.Symbol.Namespace ?? string.Empty;
                _symbolDescriptionForDialog = "";
                _duplicateSymbolDialog.ShowNextFrame();
            }

            if (ImGui.MenuItem("Combine into new type...", someOpsSelected))
            {
                _nameSpaceForDialogEdits = compositionOp.Symbol.Namespace ?? string.Empty;
                _symbolDescriptionForDialog = "";
                _combineToSymbolDialog.ShowNextFrame();
            }

            ImGui.EndMenu();
        }
        //}

        var symbolPackage = compositionSymbolUi.Symbol.SymbolPackage;
        if (!symbolPackage.IsReadOnly)
        {
            if (ImGui.BeginMenu("Open folder..."))
            {
                if (ImGui.MenuItem("Project"))
                {
                    CoreUi.Instance.OpenWithDefaultApplication(symbolPackage.Folder);
                }

                if (ImGui.MenuItem("Resources"))
                {
                    CoreUi.Instance.OpenWithDefaultApplication(symbolPackage.ResourcesFolder);
                }

                ImGui.EndMenu();
            }
        }

        if (ImGui.BeginMenu("Add..."))
        {
            if (ImGui.MenuItem("Add Node...", "TAB", false, true))
            {
                _window.SymbolBrowser.OpenAt(InverseTransformPositionFloat(clickPosition), null, null, false);
            }

            if (canModify)
            {
                if (ImGui.MenuItem("Add input parameter..."))
                {
                    _addInputDialog.ShowNextFrame();
                }

                if (ImGui.MenuItem("Add output..."))
                {
                    _addOutputDialog.ShowNextFrame();
                }
            }

            if (ImGui.MenuItem("Add Annotation",
                               shortcut: KeyboardBinding.ListKeyboardShortcuts(UserActions.AddAnnotation, false),
                               selected: false,
                               enabled: true))
            {
                var newAnnotation = NodeActions.AddAnnotation(NodeSelection, this, compositionOp);
                _graph.RenameAnnotation(newAnnotation);
            }

            ImGui.EndMenu();
        }

        ImGui.Separator();

        if (ImGui.MenuItem("Export as Executable", oneOpSelected))
        {
            switch (PlayerExporter.TryExportInstance(compositionOp, selectedChildUis.Single(), out var reason, out var exportDir))
            {
                case false:
                    Log.Error(reason);
                    BlockingWindow.Instance.ShowMessageBox(reason, $"Failed to export {label}");
                    break;
                default:
                    Log.Info(reason);
                    BlockingWindow.Instance.ShowMessageBox(reason, $"Exported {label} successfully!");
                    // open export directory in native file explorer
                    CoreUi.Instance.OpenWithDefaultApplication(exportDir);
                    break;
            }
        }

        if (oneOpSelected)
        {
            var symbol = selectedChildUis.Single().SymbolChild.Symbol;
            CustomComponents.DrawSymbolCodeContextMenuItem(symbol);
            var childUi = selectedChildUis.Single();

            // get instance that is currently selected
            var instance = compositionOp.Children[childUi.Id];

            if (NodeActions.TryGetShaderPath(instance, out var filePath, out var owner))
            {
                var shaderIsReadOnly = owner.IsReadOnly;

                if (ImGui.MenuItem("Open in Shader Editor", true))
                {
                    if (shaderIsReadOnly)
                    {
                        CopyToTempShaderPath(filePath, out filePath);
                        BlockingWindow.Instance.ShowMessageBox("Warning - viewing a read-only shader. Modifications will not be saved.\n" +
                                                               "Following #include directives outside of the temp folder may lead you to read-only files, " +
                                                               "and editing those can break operators.\n\nWith great power...", "Warning");
                    }

                    EditorUi.Instance.OpenWithDefaultApplication(filePath);
                }
            }
        }
    }

    private static void CopyToTempShaderPath(string filePath, out string newFilePath)
    {
        var directory = Path.GetDirectoryName(filePath)!;
        var destinationDirectory = Path.Combine(UserData.TempFolder, "ReadOnlyShaders");

        if (Directory.Exists(destinationDirectory))
        {
            try
            {
                Directory.Delete(destinationDirectory, true);
            }
            catch (Exception e)
            {
                Log.Warning($"Failed to delete temp directory: {e}");
            }
        }

        var directoryInfo = Directory.CreateDirectory(destinationDirectory);

        // copy all files in directory to temp directory for intellisense to work
        var allFilesInDirectory = Directory.EnumerateFiles(directory);
        FileInfo copiedFile;
        foreach (var file in allFilesInDirectory)
        {
            var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(file));
            File.Copy(file, destinationPath);
        }

        ShaderLinter.AddPackage(new ShaderCompiler.ShaderResourcePackage(directoryInfo), ResourceManager.SharedShaderPackages,
                                replaceExisting: true);
        newFilePath = Path.Combine(destinationDirectory, Path.GetFileName(filePath));
    }

    private bool _contextMenuIsOpen;

    private void DrawGrid(ImDrawListPtr drawList)
    {
        var gridSize = Math.Abs(64.0f * Scale.X);
        for (var x = (-Scroll.X * Scale.X) % gridSize; x < WindowSize.X; x += gridSize)
        {
            drawList.AddLine(new Vector2(x, 0.0f) + WindowPos,
                             new Vector2(x, WindowSize.Y) + WindowPos, UiColors.CanvasGrid);
        }

        for (var y = (-Scroll.Y * Scale.Y) % gridSize; y < WindowSize.Y; y += gridSize)
        {
            drawList.AddLine(
                             new Vector2(0.0f, y) + WindowPos,
                             new Vector2(WindowSize.X, y) + WindowPos, UiColors.CanvasGrid);
        }
    }

    public IEnumerable<ISelectableCanvasObject> SelectableChildren => NodeSelection.GetSelectableChildren(_window.CompositionOp);

    //private readonly List<ISelectableCanvasObject> _selectableItems = new();
    #endregion

    #region public API
    public bool Destroyed { get; private set; }
    public GraphWindow Window => _window;

    public void OpenAndFocusInstance(IReadOnlyList<Guid> path)
    {
        if (path.Count == 1)
        {
            _window.TrySetCompositionOp(path, ICanvas.Transition.JumpOut, path[0]);
            return;
        }

        var compositionPath = path.Take(path.Count - 1).ToList();
        _window.TrySetCompositionOp(compositionPath, ICanvas.Transition.JumpIn, path[^1]);
    }
    #endregion

    private readonly SelectionFence _selectionFence = new();
    private readonly AddInputDialog _addInputDialog = new();
    private readonly AddOutputDialog _addOutputDialog = new();
    private readonly CombineToSymbolDialog _combineToSymbolDialog = new();
    private readonly DuplicateSymbolDialog _duplicateSymbolDialog = new();
    private readonly RenameSymbolDialog _renameSymbolDialog = new();
    public readonly EditNodeOutputDialog EditNodeOutputDialog = new();
    public readonly EditCommentDialog EditCommentDialog = new();
    public readonly LibWarningDialog LibWarningDialog = new();

    private string _symbolNameForDialogEdits = "";
    private string _symbolDescriptionForDialog = "";
    private string _nameSpaceForDialogEdits = "";
    private readonly GraphWindow _window;
    private Vector2 _dampedScrollVelocity = Vector2.Zero;
    internal readonly NavigationHistory NavigationHistory;
    internal readonly Structure Structure;
    internal readonly NodeNavigation NodeNavigation;
    internal readonly NodeSelection NodeSelection;
    private readonly NodeGraphLayouting _nodeGraphLayouting;
    private readonly Graph _graph;
    internal readonly SelectableNodeMovement SelectableNodeMovement;
}

public enum GraphHoverModes
{
    Disabled,
    Live,
    LastValue,
}