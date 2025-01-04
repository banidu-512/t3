﻿using System.Diagnostics;
using ImGuiNET;
using T3.Editor.Gui.Commands.Graph;
using T3.Editor.Gui.Graph;
using T3.Editor.Gui.Graph.Dialogs;
using T3.Editor.Gui.Graph.Helpers;
using T3.Editor.Gui.MagGraph.Interaction;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.UiHelpers;
using MagItemMovement = T3.Editor.Gui.MagGraph.Interaction.MagItemMovement;

// ReSharper disable MemberCanBePrivate.Global

namespace T3.Editor.Gui.MagGraph.States;

internal static class GraphStates
{
    internal static State Default
        = new(
              Enter: context =>
                     {
                         // Todo: this should be a reset method in context
                         context.TempConnections.Clear();
                         context.ActiveSourceItem = null;
                         context.DraggedPrimaryOutputType = null;

                         // ReSharper disable once ConstantConditionalAccessQualifier
                         // This might not be initialized on startup
                         context.Placeholder.Reset(context);
                         context.DisconnectedInputsHashes.Clear();
                     },
              Update: context =>
                      {
                          if (context.ItemWithActiveCustomUi != null)
                              return;

                          // Check keyboard commands if focused...
                          if (context.Canvas.IsFocused
                              && context.Canvas.IsHovered
                              && !ImGui.IsAnyItemActive())
                          {
                              // Tab create placeholder
                              if (ImGui.IsKeyReleased(ImGuiKey.Tab))
                              {
                                  var focusedObject =
                                      context.Selector.Selection.Count == 1 &&
                                      context.Canvas.IsItemVisible(context.Selector.Selection[0])
                                          ? context.Selector.Selection[0]
                                          : null;

                                  if (focusedObject != null
                                      && context.Layout.Items.TryGetValue(focusedObject.Id, out var focusedItem))
                                  {
                                      if (focusedItem.OutputLines.Length > 0)
                                      {
                                          context.Placeholder.OpenForItem(context, focusedItem, focusedItem.OutputLines[0]);
                                      }
                                  }
                                  else
                                  {
                                      var posOnCanvas = context.Canvas.InverseTransformPositionFloat(ImGui.GetMousePos());
                                      context.Placeholder.OpenOnCanvas(context, posOnCanvas);
                                  }

                                  context.StateMachine.SetState(Placeholder, context);
                              }

                              // else if (ImGui.IsKeyReleased(ImGuiKey.Delete) || ImGui.IsKeyReleased(ImGuiKey.Backspace))
                              // {
                              //     Modifications.DeleteSelectedOps(context);
                              // }
                          }

                          if (!context.Canvas.IsHovered)
                              return;

                          // Mouse click
                          var clickedDown = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
                          if (!clickedDown)
                              return;

                          if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                          {
                              if (GraphWindow.Focused != null)
                              {
                                  var clickedBackground = context.ActiveItem == null;
                                  if (clickedBackground)
                                  {
                                      GraphWindow.Focused?.Components.TrySetCompositionOpToParent();
                                  }
                                  else
                                  {
                                      if (ImGui.IsWindowFocused() || ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
                                      {
                                          // Instance might not be available for input and output nodes
                                          if (context.ActiveItem.Variant == MagGraphItem.Variants.Operator && context.ActiveItem.Instance != null)
                                          {
                                              var blocked = false;
                                              // FIXME: implement
                                              // if (UserSettings.Config.WarnBeforeLibEdit && context.ActiveItem.Instance.Symbol.Namespace.StartsWith("Lib."))
                                              // {
                                              //     if (UserSettings.Config.WarnBeforeLibEdit)
                                              //     {
                                              //         var count = Structure.CollectDependingSymbols(instance.Symbol).Count();
                                              //         LibWarningDialog.DependencyCount = count;
                                              //         LibWarningDialog.HandledInstance = instance;
                                              //         _canvas.LibWarningDialog.ShowNextFrame();
                                              //         blocked = true;
                                              //     }
                                              // }

                                              if (!blocked)
                                              {
                                                  // Until we align the context switching between graphs, this hack applies the current
                                                  // MagGraph scope to the legacy graph, so it's correctly saved for the Symbol in the user settings...
                                                  GraphWindow.Focused.GraphCanvas.SetTargetScope(context.Canvas.GetTargetScope());
                                                  GraphWindow.Focused.Components.GraphCanvas.SetCompositionToChildInstance(context.ActiveItem.Instance);
                                                  ImGui.CloseCurrentPopup(); // ?? 
                                              }
                                          }
                                      }
                                  }
                              }
                          }

                          if (context.ActiveItem == null)
                          {
                              context.StateMachine.SetState(HoldBackground, context);
                          }
                          else
                          {
                              var isHoveringOutput = context.ActiveSourceOutputId != Guid.Empty;
                              if (isHoveringOutput)
                              {
                                  context.StateMachine.SetState(HoldOutput, context);
                                  context.ActiveSourceItem = context.ActiveItem;
                              }
                              else
                              {
                                  context.StateMachine.SetState(HoldItem, context);
                              }
                          }
                      },
              Exit:
              _ => { }
             );

    /// <summary>
    /// Active while long tapping on background for insertion
    /// </summary>
    internal static State HoldBackground
        = new(
              Enter: _ => { },
              Update: context =>
                      {
                          if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)
                              || !context.Canvas.IsFocused
                              || ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                          {
                              context.StateMachine.SetState(Default, context);
                              return;
                          }

                          const float longTapDuration = 0.3f;
                          var longTapProgress = context.StateMachine.StateTime / longTapDuration;
                          MagItemMovement.UpdateLongPressIndicator(longTapProgress);

                          if (!(longTapProgress > 1))
                              return;

                          // TODO: setting both, state and placeholder, feels awkward.
                          context.StateMachine.SetState(Placeholder, context);
                          var posOnCanvas = context.Canvas.InverseTransformPositionFloat(ImGui.GetMousePos());
                          context.Placeholder.OpenOnCanvas(context, posOnCanvas);
                      },
              Exit: _ => { }
             );

    internal static State Placeholder
        = new(
              Enter: _ => { },
              Update: context =>
                      {
                          if (context.Placeholder.PlaceholderItem != null)
                              return;

                          context.Placeholder.Cancel(context);
                          context.StateMachine.SetState(Default, context);
                      },
              Exit: _ => { }
             );

    internal static State HoldItem
        = new(
              Enter: context =>
                     {
                         var item = context.ActiveItem;
                         Debug.Assert(item != null);

                         var selector = context.Selector;

                         var isPartOfSelection = selector.IsSelected(item);
                         if (isPartOfSelection)
                         {
                             context.ItemMovement.SetDraggedItems(selector.Selection);
                         }
                         else
                         {
                             context.ItemMovement.SetDraggedItemIdsToSnappedForItem(item);
                         }
                     },
              Update: context =>
                      {
                          Debug.Assert(context.ActiveItem != null);

                          if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                          {
                              MagItemMovement.SelectActiveItem(context);
                              context.ItemMovement.Reset();
                              context.StateMachine.SetState(Default, context);
                              return;
                          }

                          if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                          {
                              context.StateMachine.SetState(DragItems, context);
                              return;
                          }

                          const float longTapDuration = 0.3f;
                          var longTapProgress = context.StateMachine.StateTime / longTapDuration;
                          MagItemMovement.UpdateLongPressIndicator(longTapProgress);

                          if (!(longTapProgress > 1))
                              return;

                          MagItemMovement.SelectActiveItem(context);
                          context.ItemMovement.SetDraggedItemIds([context.ActiveItem.Id]);
                          context.StateMachine.SetState(HoldItemAfterLongTap, context);
                      },
              Exit: _ => { }
             );

    internal static State HoldItemAfterLongTap
        = new(
              Enter: _ => { },
              Update: context =>
                      {
                          Debug.Assert(context.ActiveItem != null);

                          if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                          {
                              MagItemMovement.SelectActiveItem(context);
                              context.StateMachine.SetState(Default, context);
                              return;
                          }

                          if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                          {
                              context.StateMachine.SetState(DragItems, context);
                          }
                      },
              Exit: _ => { }
             );

    internal static State DragItems
        = new(
              Enter: context =>
                     {
                         context.ItemMovement.PrepareDragInteraction();
                         context.ItemMovement.StartDragOperation(context);
                     },
              Update: context =>
                      {
                          if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                          {
                              context.ItemMovement.CompleteDragOperation(context);

                              context.StateMachine.SetState(Default, context);
                              return;
                          }

                          context.ItemMovement.UpdateDragging(context);
                      },
              Exit: context => { context.ItemMovement.StopDragOperation(); }
             );

    /// <summary>
    /// Active while long tapping on background for insertion
    /// </summary>
    internal static State HoldOutput
        = new(
              Enter: _ => { },
              Update: context =>
                      {
                          var sourceItem = context.ActiveItem;
                          Debug.Assert(sourceItem != null);
                          Debug.Assert(sourceItem.OutputLines.Length > 0);
                          Debug.Assert(context.ActiveSourceOutputId != Guid.Empty);

                          // Click
                          if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                          {
                              if (context.TryGetActiveOutputLine(out var outputLine))
                              {
                                  context.Placeholder.OpenForItem(context, sourceItem, outputLine, context.ActiveOutputDirection);
                                  context.StateMachine.SetState(Placeholder, context);
                              }
                              else
                              {
                                  context.StateMachine.SetState(Default, context);
                              }

                              return;
                          }

                          // Start dragging...
                          if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                          {
                              if (!context.TryGetActiveOutputLine(out var outputLine))
                              {
                                  Log.Warning("Output not found?");
                                  context.StateMachine.SetState(Default, context);
                                  return;
                              }

                              //var outputLine = context.GetActiveOutputLine();              
                              var output = outputLine.Output;
                              var posOnCanvas = sourceItem.PosOnCanvas + new Vector2(MagGraphItem.GridSize.X,
                                                                                     MagGraphItem.GridSize.Y * (1.5f + outputLine.VisibleIndex));

                              var tempConnection = new MagGraphConnection
                                                       {
                                                           Style = MagGraphConnection.ConnectionStyles.Unknown,
                                                           SourcePos = posOnCanvas,
                                                           TargetPos = default,
                                                           SourceItem = sourceItem,
                                                           TargetItem = null,
                                                           SourceOutput = output,
                                                           OutputLineIndex = outputLine.VisibleIndex,
                                                           VisibleOutputIndex = 0,
                                                           ConnectionHash = 0,
                                                           IsTemporary = true,
                                                       };
                              context.TempConnections.Add(tempConnection);
                              context.ActiveSourceItem = sourceItem;
                              context.DraggedPrimaryOutputType = output.ValueType;
                              context.StateMachine.SetState(DragConnectionEnd, context);
                          }
                      },
              Exit: _ => { }
             );

    internal static State DragConnectionEnd
        = new(
              Enter: _ => { },
              Update: context =>
                      {
                          if (ImGui.IsKeyDown(ImGuiKey.Escape))
                          {
                              context.StateMachine.SetState(Default, context);
                              return;
                          }

                          var posOnCanvas = context.Canvas.InverseTransformPositionFloat(ImGui.GetMousePos());
                          context.PeekAnchorInCanvas = posOnCanvas;

                          var mouseReleased = !ImGui.IsMouseDown(ImGuiMouseButton.Left);
                          if (!mouseReleased)
                              return;

                          if (InputSnapper.TryToReconnect(context))
                          {
                              context.Layout.FlagAsChanged();
                              context.CompleteMacroCommand();
                              context.StateMachine.SetState(Default, context);
                              return;
                          }

                          var hasDisconnections = context.TempConnections.Any(c => c.WasDisconnected);

                          var droppedOnItem = InputPicking.TryInitializeAtPosition(context, posOnCanvas);
                          if (droppedOnItem)
                          {
                              context.StateMachine.SetState(PickInput, context);
                          }
                          else if (hasDisconnections)
                          {
                              // Ripped off input -> Avoid open place holder
                              //UndoRedoStack.Add(context.MacroCommand);
                              context.CompleteMacroCommand();
                              context.StateMachine.SetState(Default, context);
                          }
                          else
                          {
                              // Was dropped on operator or background...
                              context.Placeholder.OpenOnCanvas(context, posOnCanvas, context.DraggedPrimaryOutputType);
                              context.StateMachine.SetState(Placeholder, context);
                          }
                      },
              Exit: _ => { }
             );

    internal static State PickInput
        = new(
              Enter: _ => { },
              Update: context => { InputPicking.DrawHiddenInputSelector(context); },
              Exit: _ => { }
             );

    internal static State HoldingConnectionEnd
        = new(
              Enter: _ => { },
              Update: context =>
                      {
                          if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                          {
                              context.Placeholder.OpenToSplitHoveredConnections(context); // Will change state implicitly
                              return;
                          }

                          if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                          {
                              if (context.ConnectionHovering.ConnectionHoversWhenClicked.Count == 0)
                                  return;

                              var connection = context.ConnectionHovering.ConnectionHoversWhenClicked[0].Connection;
                              context.DisconnectedInputsHashes.Add(connection.GetItemInputHash()); // keep input visible until state is complete

                              // Remove existing connection
                              context.StartMacroCommand("Reconnect from input")
                                     .AddAndExecCommand(new DeleteConnectionCommand(context.CompositionOp.Symbol,
                                                                                    connection.AsSymbolConnection(),
                                                                                    connection.MultiInputIndex));

                              var tempConnection = new MagGraphConnection
                                                       {
                                                           Style = MagGraphConnection.ConnectionStyles.Unknown,
                                                           SourcePos = connection.SourcePos,
                                                           TargetPos = default,
                                                           SourceItem = connection.SourceItem,
                                                           TargetItem = null,
                                                           SourceOutput = connection.SourceOutput,
                                                           OutputLineIndex = 0,
                                                           VisibleOutputIndex = 0,
                                                           ConnectionHash = 0,
                                                           IsTemporary = true,
                                                           WasDisconnected = true,
                                                       };

                              context.TempConnections.Add(tempConnection);
                              context.ActiveSourceItem = connection.SourceItem;
                              context.DraggedPrimaryOutputType = connection.Type;
                              context.ActiveItem = connection.SourceItem;
                              context.StateMachine.SetState(DragConnectionEnd, context);
                              context.Layout.FlagAsChanged();
                          }
                      },
              Exit: _ => { }
             );

    internal static State HoldingConnectionBeginning
        = new(
              Enter: _ => { },
              Update: context =>
                      {
                          if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                          {
                              context.Placeholder.OpenToSplitHoveredConnections(context); // Will change state implicitly
                              return;
                          }

                          if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                          {
                              if (context.ConnectionHovering.ConnectionHoversWhenClicked.Count == 0)
                                  return;

                              context.StartMacroCommand("Reconnect from output");

                              foreach (var h in context.ConnectionHovering.ConnectionHoversWhenClicked
                                                       .OrderByDescending(h => h.Connection.MultiInputIndex))
                              {
                                  var connection = h.Connection;

                                  context.DisconnectedInputsHashes.Add(connection.GetItemInputHash()); // keep input visible until state is complete

                                  // Remove existing connections
                                  context.MacroCommand!
                                         .AddAndExecCommand(new DeleteConnectionCommand(context.CompositionOp.Symbol,
                                                                                        connection.AsSymbolConnection(),
                                                                                        h.Connection.MultiInputIndex));

                                  if (connection.MultiInputIndex > 0)
                                      continue;

                                  var tempConnection = new MagGraphConnection
                                                           {
                                                               Style = MagGraphConnection.ConnectionStyles.Unknown,
                                                               TargetPos = connection.TargetPos,
                                                               TargetItem = connection.TargetItem,
                                                               InputLineIndex = connection.InputLineIndex,
                                                               MultiInputIndex = connection.MultiInputIndex,
                                                               SourceItem = null,
                                                               SourceOutput = null,
                                                               IsTemporary = true,
                                                               WasDisconnected = true,
                                                           };

                                  // Sadly keeping disconnected multi input slots visible is tricky,
                                  // so, this is only a preparation for a potential later implementation
                                  //Log.Debug("Keep input hash " + connection.GetItemInputHash());
                                  //context.DisconnectedInputsHashes.Add(connection.GetItemInputHash());
                                  context.TempConnections.Add(tempConnection);
                                  context.DraggedPrimaryOutputType = connection.Type;
                              }

                              context.Layout.FlagAsChanged();
                              context.StateMachine.SetState(DragConnectionBeginning, context);
                          }
                      },
              Exit:
              _ => { }
             );

    internal static State DragConnectionBeginning
        = new(
              Enter: _ => { },
              Update: context =>
                      {
                          if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                          {
                              if (OutputSnapper.TryToReconnect(context))
                              {
                                  context.Layout.FlagAsChanged();
                              }

                              context.CompleteMacroCommand();
                              context.StateMachine.SetState(Default, context);
                              return;
                          }

                          if (ImGui.IsKeyDown(ImGuiKey.Escape))
                          {
                              context.CancelMacroCommand();
                              context.Layout.FlagAsChanged();
                              context.StateMachine.SetState(Default, context);
                          }
                      },
              Exit: _ => { });
}