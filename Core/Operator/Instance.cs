﻿#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using T3.Core.Logging;
using T3.Core.Model;
using T3.Core.Operator.Slots;
using T3.Core.Resource;
using T3.Core.Utils;

namespace T3.Core.Operator
{
    public abstract class Instance :  IGuidPathContainer, IResourceConsumer
    {
        public abstract Type Type { get; }

        public Guid SymbolChildId { get; private set; } = Guid.Empty;

        internal void SetChildId(Guid symbolChildId)
        {
            if(SymbolChildId != Guid.Empty)
                throw new InvalidOperationException("Instance already has a symbol child");
            
            SymbolChildId = symbolChildId;
        }

        public Symbol.Child? SymbolChild => _parent?.Symbol.Children[SymbolChildId];

        private Instance? _parent;

        public Instance? Parent
        {
            get => _parent;
            internal set
            {
                _parent = value;
                _resourceFoldersDirty = true;
            }
        }
        
        SymbolPackage IResourceConsumer.Package => Symbol.SymbolPackage;
        public event Action? Disposing;

        public abstract Symbol Symbol { get; }

        private readonly List<ISlot> _outputs = new();
        public readonly IReadOnlyList<ISlot> Outputs;

        internal readonly Dictionary<Guid, Instance> ChildInstances = new();
        public readonly IReadOnlyDictionary<Guid, Instance> Children;
        private readonly List<IInputSlot> _inputs = new();
        public readonly IReadOnlyList<IInputSlot> Inputs;

        public IReadOnlyList<IResourcePackage> AvailableResourcePackages
        {
            get
            {
                GatherResourcePackages(this, ref _availableResourcePackages);
                return _availableResourcePackages;
            }
        }

        /// <summary>
        /// get input without GC allocations 
        /// </summary>
        public IInputSlot? GetInput(Guid guid)
        {
            //return Inputs.SingleOrDefault(input => input.Id == guid);
            var inputCount = Inputs.Count;
            for (var index = 0; index < inputCount; index++)
            {
                var i = Inputs[index];
                if (i.Id == guid)
                    return i;
            }

            return null;
        }

        protected Instance()
        {
            Outputs = _outputs;
            Inputs = _inputs;
            Children = ChildInstances;
        }

        internal void Dispose()
        {
            Disposing?.Invoke();
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
        }

        protected void SetupInputAndOutputsFromType()
        {
            var symbol = SymbolRegistry.SymbolsByType[Type];
            var assemblyInfo = symbol.SymbolPackage.AssemblyInformation;
            var operatorTypeInfo = assemblyInfo.OperatorTypeInfo[symbol.Id];
            foreach (var input in operatorTypeInfo.Inputs)
            {
                var attribute = input.Attribute;
                var inputSlot = (IInputSlot)input.Field.GetValue(this);
                inputSlot!.Parent = this;
                inputSlot.Id = attribute.Id;
                inputSlot.MappedType = attribute.MappedType;
                _inputs.Add(inputSlot);
            }

            // outputs identified by attribute
            foreach (var output in operatorTypeInfo.Outputs)
            {
                var slot = (ISlot)output.Field.GetValue(this);
                slot!.Parent = this;
                slot.Id = output.Attribute.Id;
                _outputs.Add(slot);
            }
        }

        internal bool TryAddConnection(Symbol.Connection connection, int multiInputIndex)
        {
            var gotSource = TryGetSourceSlot(connection, out var sourceSlot);
            var gotTarget = TryGetTargetSlot(connection, out var targetSlot);

            if (!gotSource || !gotTarget)
                return false;

            targetSlot.AddConnection(sourceSlot, multiInputIndex);
            sourceSlot.DirtyFlag.Invalidate();
            return true;
        }

        public void RemoveConnection(Symbol.Connection connection, int index)
        {
            if (!TryGetTargetSlot(connection, out var targetSlot))
            {
                return;
            }

            targetSlot.RemoveConnection(index);
        }

        private bool TryGetSourceSlot(Symbol.Connection connection, out ISlot sourceSlot)
        {
            var compositionInstance = this;

            // Get source Instance
            Instance sourceInstance = null;
            var gotSourceInstance = false;
            
            var sourceParentOrChildId = connection.SourceParentOrChildId;
            
            foreach(var child in compositionInstance.Children.Values)
            {
                if (child.SymbolChildId != sourceParentOrChildId)
                    continue;
                
                sourceInstance = child;
                gotSourceInstance = true;
                break;
            }

            // Evaluate correctness of slot source Instance
            var connectionBelongsToThis = sourceParentOrChildId == Guid.Empty;
            if (!gotSourceInstance && !connectionBelongsToThis)
            {
                Log.Error($"Connection in {this} has incorrect source slot: {sourceParentOrChildId}");
                sourceSlot = null;
                return false;
            }

            // Get source Slot
            var sourceSlotList = gotSourceInstance ? sourceInstance.Outputs : compositionInstance.Inputs.Cast<ISlot>();
            sourceSlot = null;
            var gotSourceSlot = false;
            
            foreach(var slot in sourceSlotList)
            {
                if (slot.Id != connection.SourceSlotId)
                    continue;
                
                sourceSlot = slot;
                gotSourceSlot = true;
                break;
            }
            
            return gotSourceSlot;
        }

        private bool TryGetTargetSlot(Symbol.Connection connection, out ISlot targetSlot)
        {
            var compositionInstance = this;

            // Get target Instance
            
            var targetParentOrChildId = connection.TargetParentOrChildId;

            // Get target Slot
            var targetSlotList = compositionInstance.Children.TryGetValue(targetParentOrChildId, out var targetInstance) 
                                     ? targetInstance.Inputs.Cast<ISlot>() 
                                     : compositionInstance.Outputs;

            targetSlot = null;
            var gotTargetSlot = false;
            foreach(var slot in targetSlotList)
            {
                if (slot.Id != connection.TargetSlotId)
                    continue;
                
                targetSlot = slot;
                gotTargetSlot = true;
                break;
            }

            #if DEBUG
            if (targetInstance == null)
            {
                System.Diagnostics.Debug.Assert(connection.TargetParentOrChildId == Guid.Empty);
            }
            #endif

            return gotTargetSlot;
        }

        private static void GatherResourcePackages(Instance instance, ref List<SymbolPackage> resourceFolders)
        {
            if (!instance._resourceFoldersDirty)
                return;
            
            instance._resourceFoldersDirty = false;
            if(resourceFolders != null)
                resourceFolders.Clear();
            else
                resourceFolders = [];
            
            while (instance != null)
            {
                var package = instance.Symbol.SymbolPackage;
                if (!resourceFolders.Contains(package))
                {
                    resourceFolders.Add(package);
                }

                instance = instance._parent;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected internal bool TryGetFilePath(string relativePath, out string absolutePath, bool isFolder = false)
        {
            return ResourceManager.TryResolvePath(relativePath, this, out absolutePath, out _, isFolder);
        }
        
        
        internal static void SortInputSlotsByDefinitionOrder(Instance instance)
        {
            // order the inputs by the given input definitions. original order is coming from code, but input def order is the relevant one
            var inputs = instance._inputs;
            var inputDefinitions = instance.Symbol.InputDefinitions;
            int numInputs = inputs.Count;
            var lastIndex = numInputs - 1;

            for (int i = 0; i < lastIndex; i++)
            {
                Guid inputId = inputDefinitions[i].Id;
                if (inputs[i].Id != inputId)
                {
                    int index = inputs.FindIndex(i + 1, input => input.Id == inputId);
                    Debug.Assert(index >= 0);
                    inputs.Swap(i, index);
                    Debug.Assert(inputId == inputs[i].Id);
                }
            }

            #if DEBUG
            if (numInputs > 0)
            {
                Debug.Assert(inputs.Count == inputDefinitions.Count);
            }
            #endif
        }

        public IReadOnlyList<Guid> InstancePath => OperatorUtils.BuildIdPathForInstance(this);

        private List<SymbolPackage> _availableResourcePackages;
        private bool _resourceFoldersDirty = true;


        internal static void AddChildTo(Instance parentInstance, Instance childInstance)
        {
            parentInstance.ChildInstances.Add(childInstance.SymbolChildId, childInstance);
        }

        public sealed override string ToString()
        {
            const string fmt = "{0} ({1})";
            return _asString ??= string.Format(fmt, GetType().Name, SymbolChildId.ToString());
        }

        private string? _asString;
    }

    public class Instance<T> : Instance where T : Instance
    {
        private static readonly Type _staticType = typeof(T);
        
        // this intended to be a different symbol per-type
        // ReSharper disable once StaticMemberInGenericType
        private static readonly Symbol StaticSymbol = SymbolRegistry.SymbolsByType[_staticType];
        
        public sealed override Type Type => _staticType;
        public sealed override Symbol Symbol => StaticSymbol;

        protected Instance()
        {
            SetupInputAndOutputsFromType();
        }
    }
}