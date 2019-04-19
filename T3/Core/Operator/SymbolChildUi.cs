﻿using imHelpers;
using System.Collections.Generic;
using System.Numerics;
using T3.graph;
using T3.Gui.graph;
using T3.Gui.Selection;

namespace T3.Core.Operator
{
    /// <summary>
    /// Properties needed for visual representation of an instance. Should later be moved to gui component.
    /// </summary>
    public class SymbolChildUi : IStackable, IConnectionSource, IConnectionTarget
    {
        public SymbolChild SymbolChild;
        public Vector2 Position { get; set; } = Vector2.Zero;
        public Vector2 Size { get; set; } = new Vector2(100, 30);
        public bool IsVisible { get; set; } = true;
        public bool IsSelected { get; set; } = false;
        public string Name { get; set; } = string.Empty;
        public string ReadableName => string.IsNullOrEmpty(Name) ? SymbolChild.Symbol.SymbolName : Name;

        public bool IsStackableAbove => true; // { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public bool IsStackableBelow => true; //{ get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        // These could be stateless and initialized by a helper
        public List<ConnectionLine> ConnectionLinesOut { get; set; } = new List<ConnectionLine>();
        public List<ConnectionLine> ConnectionLinesIn { get; set; } = new List<ConnectionLine>();


        public float GetHorizontalOverlapWith(ISelectable element)
        {
            //ToDo: Implement
            return 1;
        }

        public List<IConnectable> GetOpsConnectedToInputs()
        {
            // FIXME: convert to lync
            var r = new List<IConnectable>();
            foreach (var c in ConnectionLinesIn)
            {
                r.Add(c.SourceItem);
            }
            return r;
        }

        public List<IConnectable> GetOpsConnectedToOutputs()
        {
            // FIXME: convert to lync
            var r = new List<IConnectable>();
            foreach (var c in ConnectionLinesOut)
            {
                r.Add(c.TargetItem);
            }
            return r;
        }


        public ImRect GetRangeForInputConnectionLine(SymbolChild.Input input, int multiInputIndex, bool insertConnection = false)
        {
            var slots = GetVisibileInputSlots();

            VisibleInputSlot matchingSlot = null;
            foreach (var slot in slots)
            {
                if (slot.Input == input && slot.MultiInputIndex == multiInputIndex)
                {
                    if (!insertConnection && slot.InsertAtMultiInputIndex)
                        continue;

                    matchingSlot = slot;
                    break;
                }
            }

            // Animations on non-relevant paraemters don't have a matching zone...
            if (matchingSlot == null)
            {
                return new ImRect(0, 0, 0, 0);
            }

            var minX = matchingSlot.XInItem;
            var maxX = matchingSlot.XInItem + matchingSlot.Width;
            return new ImRect(minX, GraphCanvasWindow.GridSize, maxX - minX, 0);
        }


        public List<VisibleInputSlot> GetVisibileInputSlots()
        {
            var zones = new List<VisibleInputSlot>();

            // First collect inputs that are relevant or connected
            var relevantOrConnectedInputs = new List<SymbolChild.Input>();
            foreach (var i in this.SymbolChild.InputValues.Values)
            {
                if (i.SymbolInputDef.Relevance == Symbol.InputDefinition.Relevancy.Required
                || i.SymbolInputDef.Relevance == Symbol.InputDefinition.Relevancy.Relevant)
                {
                    relevantOrConnectedInputs.Add(i);
                }
                // Show non-relevant but animated inputs...
                //else
                //{
                //    if (i.Connections.Count() > 0)
                //    {
                //        var animationConnection = Animation.GetRegardingAnimationOpPart(i.Connections[0]);
                //        if (animationConnection == null)
                //        {
                //            // Add non-animated connections
                //            relevantOrConnectedInputs.Add(i);
                //        }
                //    }
                //}
            }

            const float WIDTH_OF_MULTIINPUT_ZONES = 1.0f / 3.0f;

            /* Roll out zones multi-inputs and the slots for prepending
             * a connection at the first field or inserting connections
             * between existing connections.
             *
             */
            foreach (var input in relevantOrConnectedInputs)
            {
                //var metaInput = input. .Parent.GetMetaInput(input);
                //if (metaInput.IsMultiInput)
                //{
                //    if (!input.Connections.Any())
                //    {
                //        // empty multi-input
                //        zones.Add(new VisibleInputArea()
                //        {
                //            InputDefinition = input,
                //            MetaInput = metaInput,
                //            InsertAtMultiInputIndex = true,
                //        });
                //    }
                //    else
                //    {
                //        zones.Add(new VisibleInputArea()
                //        {
                //            InputDefinition = input,
                //            MetaInput = metaInput,
                //            InsertAtMultiInputIndex = true,
                //            Width = WIDTH_OF_MULTIINPUT_ZONES,
                //            MultiInputIndex = 0,
                //        });

                //        for (var multiInputIndex = 0; multiInputIndex < input.Connections.Count; ++multiInputIndex)
                //        {
                //            var connectedTo = input.Connections[multiInputIndex];

                //            // multi-input connection
                //            zones.Add(new VisibleInputArea()
                //            {
                //                InputDefinition = input,
                //                MetaInput = metaInput,
                //                Width = WIDTH_OF_MULTIINPUT_ZONES,
                //                MultiInputIndex = multiInputIndex,
                //            });
                //            zones.Add(new VisibleInputArea()
                //            {
                //                InputDefinition = input,
                //                MetaInput = metaInput,
                //                Width = WIDTH_OF_MULTIINPUT_ZONES,
                //                MultiInputIndex = multiInputIndex + 1,
                //                InsertAtMultiInputIndex = true
                //            });
                //        }
                //    }
                //}
                //else
                //{
                // Normal input
                zones.Add(new VisibleInputSlot()
                {
                    Input = input,
                    Width = 1,
                });
                //}
            }

            // Now distibute the width to the width of the operator
            float widthSum = 0;
            foreach (var zone in zones)
            {
                widthSum += zone.Width;
            }

            var x = 0f;
            for (var i = 0; i < zones.Count; ++i)
            {
                var widthInsideOp = zones[i].Width / widthSum * Size.X;
                zones[i].Width = widthInsideOp - 1; // requires zones to be a class
                zones[i].XInItem = x;
                x += widthInsideOp;
            }

            return zones;
        }

        public List<ConnectionLine> GetInputConnections()
        {
            return new List<ConnectionLine>();
        }

        public List<ConnectionLine> GetOutputConnections()
        {
            return new List<ConnectionLine>();
        }
    }
}