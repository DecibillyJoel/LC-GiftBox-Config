using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq;
using LC_GiftBox_Config.libs.HarmonyXExtensions;
using UnityEngine.SocialPlatforms;
using Dissonance;

namespace LC_GiftBox_Config.libs.ILStepper
{
    public class ILStepper
    {
        public readonly List<CodeInstruction> Instructions;
        public int CurrentIndex;
        public readonly ILGenerator Generator;
        public readonly Dictionary<int, LocalBuilder> Locals;
        public readonly Dictionary<int, Label> Labels;

        public ILStepper(IEnumerable<CodeInstruction> codes, ILGenerator generator, int index = 0)
        {
            Instructions = codes.ToList();
            CurrentIndex = index;
            Generator = generator;

            Locals = codes.Select(code => (code.operand as LocalBuilder)!).Where(local => local != null).Distinct().ToDictionary(local => local.LocalIndex, local => local);
            Labels = codes.SelectMany(code => code.labels).Distinct().ToDictionary(label => label.GetHashCode(), label => label);
        }

        public CodeInstruction CurrentInstruction => Instructions[CurrentIndex];
        public OpCode CurrentOpCode => CurrentInstruction.opcode;
        public object? CurrentOperand => CurrentInstruction.operand;

        public Label DeclareLabel() {
            Label newLabel = Generator.DefineLabel();
            Labels.Add(newLabel.GetHashCode(), newLabel);

            return newLabel;
        }

        public LocalBuilder DeclareLocal(Type type, bool pinned = false) {
            LocalBuilder newLocal = Generator.DeclareLocal(type, pinned);
            Locals.Add(newLocal.LocalIndex, newLocal);

            return newLocal;
        }

        public LocalBuilder? TryGetLocal(int localIndex)
        {
            return Locals.TryGetValue(localIndex, out LocalBuilder? local) ? local : null;
        }

        public LocalBuilder? TryGetLocal(CodeInstruction codeWithLocal)
        {
            if (!codeWithLocal.IsLdloc() && !codeWithLocal.IsStloc()) return null;
            return TryGetLocal(codeWithLocal.LocalIndex());
        }

        public LocalBuilder GetLocal(int localIndex, string errorMessage = "No such local variable!")
        {
            return TryGetLocal(localIndex)
                ?? throw new Exception($"[libs.ILTools.GetLocal] [{localIndex}] | {errorMessage}");
        }

        public LocalBuilder GetLocal(CodeInstruction codeWithLocal, string errorMessage = "No such local variable!")
        {
            return TryGetLocal(codeWithLocal)
                ?? throw new Exception($"[libs.ILTools.GetLocal] [{codeWithLocal}] | {errorMessage}");
        }

        public LocalBuilder? TrySetLocal(CodeInstruction code, LocalBuilder local)
        {
            CodeInstruction newOpcodeAndOperand;
            if (code.IsLdloc())
            {
                newOpcodeAndOperand = CodeInstructionPolyfills.LoadLocal(local, code.opcode == OpCodes.Ldloca || code.opcode == OpCodes.Ldloca_S);
            }
            else if (code.IsStloc())
            {
                newOpcodeAndOperand = CodeInstructionPolyfills.StoreLocal(local);
            }
            else
            {
                return null;
            }

            code.opcode = newOpcodeAndOperand.opcode;
            code.operand = newOpcodeAndOperand.operand == null ? null : local;

            return local;
        }

        public LocalBuilder? TrySetLocal(CodeInstruction code, int localIndex)
        {
            LocalBuilder? local = TryGetLocal(localIndex);
            if (local == null) return null;

            return TrySetLocal(code, local);
        }

        public LocalBuilder? TrySetLocal(CodeInstruction code, CodeInstruction codeWithLocal)
        {
            LocalBuilder? local = TryGetLocal(codeWithLocal);
            if (local == null) return null;

            return TrySetLocal(code, local);
        }

        public LocalBuilder SetLocal(CodeInstruction code, LocalBuilder local, string errorMessage = "Could not set local!")
        {
            return TrySetLocal(code, local)
                ?? throw new Exception($"[libs.ILTools.SetLocal] [{code}, {local}] | {errorMessage}");
        }

        public LocalBuilder SetLocal(CodeInstruction code, int localIndex, string errorMessage = "Could not set local!")
        {
            return TrySetLocal(code, localIndex)
                ?? throw new Exception($"[libs.ILTools.SetLocal] [{code}, {localIndex}] | {errorMessage}");
        }

        public LocalBuilder SetLocal(CodeInstruction code, CodeInstruction codeWithLocal, string errorMessage = "Could not set local!")
        {
            return TrySetLocal(code, codeWithLocal)
                ?? throw new Exception($"[libs.ILTools.SetLocal] [{code}, {codeWithLocal}] | {errorMessage}");
        }

        public int? TryFindIL(Func<CodeInstruction, int, bool> searchCondition, int? index = null, bool reverse = false)
        {
            for (index ??= CurrentIndex; index >= 0 && index < Instructions.Count; index += reverse ? -1 : 1)
            {
                if (searchCondition(Instructions[index.Value], index.Value)) return index;
            }

            return null;
        }

        public int? TryFindIL(Func<CodeInstruction, bool> searchCondition, int? index = null, bool reverse = false)
        {
            return TryFindIL((code, index) => searchCondition(code), index, reverse);
        }

        public int FindIL(Func<CodeInstruction, int, bool> searchCondition, int? index = null, bool reverse = false, string errorMessage = "Not found!")
        {
            index ??= CurrentIndex;
            return TryFindIL(searchCondition: searchCondition, index: index, reverse: reverse)
                ?? throw new Exception($"[libs.ILTools.FindIL] [{searchCondition}, {index}, {reverse} ({(reverse ? "forward" : "reverse")})] | {errorMessage}");
        }

        public int FindIL(Func<CodeInstruction, bool> searchCondition, int? index = null, bool reverse = false, string errorMessage = "Not found!")
        {
            index ??= CurrentIndex;
            return TryFindIL(searchCondition: searchCondition, index: index, reverse: reverse)
                ?? throw new Exception($"[libs.ILTools.FindIL] [{searchCondition}, {index}, {reverse} ({(reverse ? "forward" : "reverse")})] | {errorMessage}");
        }

        public int? TryGotoIL(Func<CodeInstruction, bool> searchCondition, int? index = null, bool reverse = false)
        {
            index = TryFindIL(searchCondition: searchCondition, index: index, reverse: reverse);
            
            CurrentIndex = index ?? CurrentIndex;
            return index;
        }

        public int? TryGotoIL(Func<CodeInstruction, int, bool> searchCondition, int? index = null, bool reverse = false)
        {
            index = TryFindIL(searchCondition: searchCondition, index: index, reverse: reverse);
            
            CurrentIndex = index ?? CurrentIndex;
            return index;
        }

        public int GotoIL(Func<CodeInstruction, bool> searchCondition, int? index = null, bool reverse = false, string errorMessage = "Not found!")
        {
            index ??= CurrentIndex;
            return CurrentIndex = TryFindIL(searchCondition: searchCondition, index: index, reverse: reverse)
                ?? throw new Exception($"[libs.ILTools.GotoIL] [{searchCondition}, {index}, {reverse} ({(reverse ? "forward" : "reverse")})] | {errorMessage}");
        }

        public int GotoIL(Func<CodeInstruction, int, bool> searchCondition, int? index = null, bool reverse = false, string errorMessage = "Not found!")
        {
            index ??= CurrentIndex;
            return CurrentIndex = TryFindIL(searchCondition: searchCondition, index: index, reverse: reverse)
                ?? throw new Exception($"[libs.ILTools.GotoIL] [{searchCondition}, {index}, {reverse} ({(reverse ? "forward" : "reverse")})] | {errorMessage}");
        }

        public int? TryFindIndex(int? index = null, int offset = 0, int leftBoundOffset = 0, int rightBoundOffset = 0)
        {
            index = Math.Clamp((index ?? CurrentIndex) + offset, leftBoundOffset, Instructions.Count - 1  + rightBoundOffset);
            if (index < 0 || index >= Instructions.Count)
            {
                return null;
            }

            return index;
        }

        public int FindIndex(int? index = null, int offset = 0, int leftBoundOffset = 0, int rightBoundOffset = 0, string errorMessage = "Out of bounds!")
        {
            index ??= CurrentIndex;
            return TryFindIndex(index: index, offset: offset, leftBoundOffset: leftBoundOffset, rightBoundOffset: rightBoundOffset)
                ?? throw new Exception($"[libs.ILTools.FindIndex] [{index + offset} ({index} + {offset}), {leftBoundOffset} (0 + {leftBoundOffset}), {(Instructions.Count - 1) + rightBoundOffset} ({Instructions.Count - 1} + {rightBoundOffset})] | {errorMessage}");
        }

        public int? TryGotoIndex(int? index = null, int offset = 0, int leftBoundOffset = 0, int rightBoundOffset = 0)
        {
            index = TryFindIndex(index: index, offset: offset, leftBoundOffset: leftBoundOffset, rightBoundOffset: rightBoundOffset);
            
            CurrentIndex = index ?? CurrentIndex;
            return index;
        }

        public int GotoIndex(int? index = null, int offset = 0, int leftBoundOffset = 0, int rightBoundOffset = 0, string errorMessage = "Out of bounds!")
        {
            index ??= CurrentIndex;
            return CurrentIndex = TryFindIndex(index: index, offset: offset, leftBoundOffset: leftBoundOffset, rightBoundOffset: rightBoundOffset)
                ?? throw new Exception($"[libs.ILTools.GotoIndex] [{index + offset} ({index} + {offset}), {leftBoundOffset} (0 + {leftBoundOffset}), {(Instructions.Count - 1) + rightBoundOffset} ({Instructions.Count - 1} + {rightBoundOffset})] | {errorMessage}");
        }

        public List<CodeInstruction>? TryInsertIL(IEnumerable<CodeInstruction> codeRange, int? index = null, bool shiftCurrentIndex = true, bool pinLabels = true, bool pinBlocks = true)
        {
            index = TryFindIndex(index: index, rightBoundOffset: 1);
            if (index == null) return null;

            codeRange = codeRange.Select(code => {
                CodeInstruction newCode = new(code);

                if (newCode.operand != null)
                {
                    LocalBuilder? local = TryGetLocal(newCode);
                    if (local != null)
                    {
                        newCode.operand = local;
                    }
                }

                return newCode;
            });

            if (index < Instructions.Count && codeRange.Count() > 0)
            {
                if (pinLabels) Instructions[index.Value].MoveLabelsTo(codeRange.First());
                if (pinBlocks) Instructions[index.Value].MoveBlocksTo(codeRange.First());
            }

            Instructions.InsertRange(index.Value, codeRange);
            
            if (shiftCurrentIndex && CurrentIndex >= index)
            {
                CurrentIndex += codeRange.Count();
            }

            return codeRange.ToList();
        }

        public List<CodeInstruction>? TryInsertIL(CodeInstruction? code, int? index = null, bool shiftCurrentIndex = true, bool pinLabels = true, bool pinBlocks = true)
        {
            return TryInsertIL(codeRange: code != null ? [code] : [], index: index, shiftCurrentIndex: shiftCurrentIndex, pinLabels: pinLabels, pinBlocks: pinBlocks);
        }

        public List<CodeInstruction> InsertIL(IEnumerable<CodeInstruction> codeRange, int? index = null, bool shiftCurrentIndex = true, bool pinLabels = true, bool pinBlocks = true, string errorMessage = "Out of bounds!")
        {
            index ??= CurrentIndex;
            return TryInsertIL(codeRange: codeRange, index: index, shiftCurrentIndex: shiftCurrentIndex, pinLabels: pinLabels, pinBlocks: pinBlocks)
                ?? throw new Exception($"[libs.ILTools.InsertIL] [{codeRange}, {index}, {shiftCurrentIndex} ({(shiftCurrentIndex ? "shift current index" : "dont shift current index")})] | {errorMessage}");
        }

        public List<CodeInstruction> InsertIL(CodeInstruction? code, int? index = null, bool shiftCurrentIndex = true, bool pinLabels = true, bool pinBlocks = true, string errorMessage = "Out of bounds!")
        {
            index ??= CurrentIndex;
            return TryInsertIL(codeRange: code != null ? [code] : [], index: index, shiftCurrentIndex: shiftCurrentIndex, pinLabels: pinLabels, pinBlocks: pinBlocks)
                ?? throw new Exception($"[libs.ILTools.InsertIL] [{code}, {index}, {shiftCurrentIndex} ({(shiftCurrentIndex ? "shift current index" : "dont shift current index")})] | {errorMessage}");
        }

        public List<CodeInstruction>? TryRemoveIL(int? startIndex = null, int endOffset = 1, bool shiftCurrentIndex = true, bool pinLabels = true, bool pinBlocks = true)
        {
            startIndex = TryFindIndex(index: startIndex, rightBoundOffset: endOffset >= 0 ? 0 : 1);
            if (startIndex == null || TryFindIndex(index: startIndex, offset: endOffset, rightBoundOffset: 1) == null) return null;

            if (endOffset < 0) (startIndex, endOffset) = (startIndex + endOffset, -endOffset);

            List<CodeInstruction> removal = Instructions.GetRange(startIndex.Value, endOffset);
            Instructions.RemoveRange(startIndex.Value, endOffset);

            if (startIndex < Instructions.Count)
            {
                removal.ForEach(code => {
                    if (pinLabels) Instructions[startIndex.Value].MoveLabelsFrom(code);
                    if (pinBlocks) Instructions[startIndex.Value].MoveBlocksFrom(code);
                });
            }

            if (shiftCurrentIndex && CurrentIndex > startIndex) {
                CurrentIndex = Math.Max(startIndex.Value, CurrentIndex - endOffset);
            }

            return removal;
        }

        public List<CodeInstruction> RemoveIL(int? startIndex = null, int endOffset = 1, bool shiftCurrentIndex = true, bool pinLabels = true, bool pinBlocks = true, string errorMessage = "Out of bounds!")
        {
            startIndex ??= CurrentIndex;
            return TryRemoveIL(startIndex: startIndex, endOffset: endOffset, shiftCurrentIndex: shiftCurrentIndex, pinLabels: pinLabels, pinBlocks: pinBlocks)
                ?? throw new Exception($"[libs.ILTools.RemoveIL] [{startIndex}, {startIndex + endOffset} ({startIndex} + {endOffset}), {shiftCurrentIndex} ({(shiftCurrentIndex ? "shift current index" : "dont shift current index")})] | {errorMessage}");
        }

        public List<CodeInstruction>? TryGetIL(int? startIndex = null, int endOffset = 1)
        {
            startIndex = TryFindIndex(index: startIndex, rightBoundOffset: endOffset >= 0 ? 0 : 1);
            if (startIndex == null || TryFindIndex(index: startIndex, offset: endOffset, rightBoundOffset: 1) == null) return null;

            if (endOffset < 0) (startIndex, endOffset) = (startIndex + endOffset, -endOffset);

            List<CodeInstruction> retrieval = Instructions.GetRange(startIndex.Value, endOffset);
            return retrieval;
        }

        public List<CodeInstruction> GetIL(int? startIndex = null, int endOffset = 1, string errorMessage = "Out of bounds!")
        {
            startIndex ??= CurrentIndex;
            return TryGetIL(startIndex: startIndex, endOffset: endOffset)
                ?? throw new Exception($"[libs.ILTools.GetIL] [{startIndex}, {startIndex + endOffset} ({startIndex} + {endOffset}))] | {errorMessage}");
        }

        public List<Label>? TryMoveAllLabels(List<CodeInstruction> sourceCodeRange, List<CodeInstruction> destinationCodeRange)
        {
            if (sourceCodeRange.Count != destinationCodeRange.Count) return null;

            return sourceCodeRange.SelectMany((sourceCode, sourceCodeIndex) => {
                List<Label> shiftingLabels = sourceCode.ExtractLabels();
                destinationCodeRange[sourceCodeIndex].labels.AddRange(shiftingLabels);
                return shiftingLabels;
            }).ToList();
        }

        public List<Label>? TryMoveAllLabels(List<CodeInstruction> sourceCodeRange, CodeInstruction destinationCode)
        {
            return TryMoveAllLabels(sourceCodeRange, Enumerable.Repeat(destinationCode, sourceCodeRange.Count).ToList());
        }

        public List<Label> MoveAllLabels(List<CodeInstruction> sourceCodeRange, List<CodeInstruction> destinationCodeRange, string errorMessage = "Source and destination instruction count don't match!")
        {
            return TryMoveAllLabels(sourceCodeRange, destinationCodeRange)
                ?? throw new Exception($"[libs.ILTools.MoveAllLabels] [{sourceCodeRange}, {destinationCodeRange}] | {errorMessage}");
        }

        public List<Label> MoveAllLabels(List<CodeInstruction> sourceCodeRange, CodeInstruction destinationCode, string errorMessage = "Source and destination instruction count don't match!")
        {
            return TryMoveAllLabels(sourceCodeRange, Enumerable.Repeat(destinationCode, sourceCodeRange.Count).ToList())
                ?? throw new Exception($"[libs.ILTools.MoveAllLabels] [{sourceCodeRange}, {{{destinationCode}, ...}}] | {errorMessage}");
        }

        public List<Label>? TryShiftAllLabels(int? startIndex = null, int endOffset = 1, int shiftBy = 1)
        {
            List<CodeInstruction>? sourceCodeRange = TryGetIL(startIndex: startIndex, endOffset: endOffset);
            if (sourceCodeRange == null) return null;

            int? destinationStartIndex = TryFindIndex(index: startIndex, offset: shiftBy, rightBoundOffset: 1);
            if (destinationStartIndex == null) return null;

            List<CodeInstruction>? destinationCodeRange = TryGetIL(startIndex: destinationStartIndex, endOffset: endOffset);
            if (destinationCodeRange == null) return null;

            return TryMoveAllLabels(sourceCodeRange, destinationCodeRange);
        }

        public List<Label> ShiftAllLabels(int? startIndex = null, int endOffset = 1, int shiftBy = 1, string errorMessage = "Source and/or destination instructions out of bounds!")
        {
            startIndex ??= CurrentIndex;
            return TryShiftAllLabels(startIndex, endOffset)
                ?? throw new Exception($"[libs.ILTools.ShiftAllLabels] [{startIndex}, {startIndex + endOffset} ({startIndex} + {endOffset}), {startIndex + shiftBy} ({startIndex} + {shiftBy}), {startIndex + endOffset + shiftBy} ({startIndex} + {endOffset} + {shiftBy})] | {errorMessage}");
        }

        public List<ExceptionBlock>? TryMoveAllBlocks(List<CodeInstruction> sourceCodeRange, List<CodeInstruction> destinationCodeRange)
        {
            if (sourceCodeRange.Count != destinationCodeRange.Count) return null;

            return sourceCodeRange.SelectMany((sourceCode, sourceCodeIndex) => {
                List<ExceptionBlock> shiftingBlocks = sourceCode.ExtractBlocks();
                destinationCodeRange[sourceCodeIndex].blocks.AddRange(shiftingBlocks);
                return shiftingBlocks;
            }).ToList();
        }

        public List<ExceptionBlock>? TryMoveAllBlocks(List<CodeInstruction> sourceCodeRange, CodeInstruction destinationCode)
        {
            return TryMoveAllBlocks(sourceCodeRange, Enumerable.Repeat(destinationCode, sourceCodeRange.Count).ToList());
        }

        public List<ExceptionBlock> MoveAllBlocks(List<CodeInstruction> sourceCodeRange, List<CodeInstruction> destinationCodeRange, string errorMessage = "Source and destination instruction count don't match!")
        {
            return TryMoveAllBlocks(sourceCodeRange, destinationCodeRange)
                ?? throw new Exception($"[libs.ILTools.MoveAllBlocks] [{sourceCodeRange}, {destinationCodeRange}] | {errorMessage}");
        }

        public List<ExceptionBlock> MoveAllBlocks(List<CodeInstruction> sourceCodeRange, CodeInstruction destinationCode, string errorMessage = "Source and destination instruction count don't match!")
        {
            return TryMoveAllBlocks(sourceCodeRange, Enumerable.Repeat(destinationCode, sourceCodeRange.Count).ToList())
                ?? throw new Exception($"[libs.ILTools.MoveAllBlocks] [{sourceCodeRange}, {{{destinationCode}, ...}}] | {errorMessage}");
        }

        public List<ExceptionBlock>? TryShiftAllBlocks(int? startIndex = null, int endOffset = 1, int shiftBy = 1)
        {
            List<CodeInstruction>? sourceCodeRange = TryGetIL(startIndex: startIndex, endOffset: endOffset);
            if (sourceCodeRange == null) return null;

            int? destinationStartIndex = TryFindIndex(index: startIndex, offset: shiftBy, rightBoundOffset: 1);
            if (destinationStartIndex == null) return null;

            List<CodeInstruction>? destinationCodeRange = TryGetIL(startIndex: destinationStartIndex, endOffset: endOffset);
            if (destinationCodeRange == null) return null;

            return TryMoveAllBlocks(sourceCodeRange, destinationCodeRange);
        }

        public List<ExceptionBlock> ShiftAllBlocks(int? startIndex = null, int endOffset = 1, int shiftBy = 1, string errorMessage = "Source and/or destination instructions out of bounds!")
        {
            startIndex ??= CurrentIndex;
            return TryShiftAllBlocks(startIndex, endOffset)
                ?? throw new Exception($"[libs.ILTools.ShiftAllBlocks] [{startIndex}, {startIndex + endOffset} ({startIndex} + {endOffset}), {startIndex + shiftBy} ({startIndex} + {shiftBy}), {startIndex + endOffset + shiftBy} ({startIndex} + {endOffset} + {shiftBy})] | {errorMessage}");
        }

        public List<Label> ExtractAllLabels(List<CodeInstruction> codeRange)
        {
            return codeRange.SelectMany(code => code.ExtractLabels()).ToList();
        }

        public List<Label>? TryExtractAllLabels(int? startIndex = null, int endOffset = 1)
        {
            List<CodeInstruction>? codeRange = TryGetIL(startIndex: startIndex, endOffset: endOffset);
            if (codeRange == null) return null;

            return ExtractAllLabels(codeRange);
        }

        public List<Label> ExtractAllLabels(int? startIndex = null, int endOffset = 1, string errorMessage = "Out of bounds!")
        {
            startIndex ??= CurrentIndex;
            return TryExtractAllLabels(startIndex, endOffset)
                ?? throw new Exception($"[libs.ILTools.ExtractAllLabels] [{startIndex}, {startIndex + endOffset} ({startIndex} + {endOffset})] | {errorMessage}");
        }

        public List<Label> ExtractAllBlocks(List<CodeInstruction> codeRange)
        {
            return codeRange.SelectMany(code => code.ExtractLabels()).ToList();
        }

        public List<Label>? TryExtractAllBlocks(int? startIndex = null, int endOffset = 1)
        {
            List<CodeInstruction>? codeRange = TryGetIL(startIndex: startIndex, endOffset: endOffset);
            if (codeRange == null) return null;

            return ExtractAllBlocks(codeRange);
        }

        public List<Label> ExtractAllBlocks(int? startIndex = null, int endOffset = 1, string errorMessage = "Out of bounds!")
        {
            startIndex ??= CurrentIndex;
            return TryExtractAllBlocks(startIndex, endOffset)
                ?? throw new Exception($"[libs.ILTools.ExtractAllBlocks] [{startIndex}, {startIndex + endOffset} ({startIndex} + {endOffset})] | {errorMessage}");
        }

        public List<CodeInstruction>? TryShiftIL(int? startIndex = null, int endOffset = 1, int shiftBy = 1, bool shiftCurrentIndex = true)
        {
            startIndex = TryFindIndex(index: startIndex, rightBoundOffset: endOffset >= 0 ? 0 : 1);
            if (startIndex == null || TryFindIndex(index: startIndex, offset: endOffset, rightBoundOffset: 1) == null) return null;

            if (endOffset < 0) (startIndex, endOffset) = (startIndex + endOffset, -endOffset);

            int? destinationStartIndex = TryFindIndex(index: startIndex, offset: shiftBy, rightBoundOffset: -endOffset + 1);
            if (destinationStartIndex == null) return null;

            // Precompute the shifted CurrentIndex if CurrentIndex is within the IL that's being shifted
            // Otherwise it will end up in the wrong position
            if (shiftCurrentIndex && CurrentIndex >= startIndex && CurrentIndex < (startIndex + endOffset))
            {
                CurrentIndex += shiftBy;
                shiftCurrentIndex = false;
            }

            // This error message should never be thrown
            string impossibleErrorMessage = $"[libs.ILTools.TryShiftIL] [{startIndex}, {startIndex + endOffset} ({startIndex} + {endOffset}), {startIndex + shiftBy} ({startIndex} + {shiftBy}), {startIndex + endOffset + shiftBy} ({startIndex} + {endOffset} + {shiftBy})] Somehow calculated invalid parameters. This should never happen!";

            List<CodeInstruction> shiftedIL = RemoveIL(startIndex: startIndex, endOffset: endOffset, shiftCurrentIndex: shiftCurrentIndex, errorMessage: impossibleErrorMessage);
            return InsertIL(shiftedIL, index: destinationStartIndex, shiftCurrentIndex: shiftCurrentIndex, errorMessage: impossibleErrorMessage);
        }

        public List<CodeInstruction> ShiftIL(int? startIndex = null, int endOffset = 1, int shiftBy = 1, bool shiftCurrentIndex = true, string errorMessage = "Source and/or destination instructions out of bounds!")
        {
            startIndex ??= CurrentIndex;
            return TryShiftIL(startIndex, endOffset, shiftBy, shiftCurrentIndex)
                ?? throw new Exception($"[libs.ILTools.ShiftIL] [{startIndex}, {startIndex + endOffset} ({startIndex} + {endOffset}), {startIndex + shiftBy} ({startIndex} + {shiftBy}), {startIndex + endOffset + shiftBy} ({startIndex} + {endOffset} + {shiftBy})] | {errorMessage}");
        }

        public List<CodeInstruction>? TryOverwriteIL(IEnumerable<CodeInstruction> codeRange, int? index = null)
        {
            if (TryRemoveIL(startIndex: index, endOffset: codeRange.Count(), shiftCurrentIndex: false) == null) return null;

            return TryInsertIL(codeRange: codeRange, index: index, shiftCurrentIndex: false);
        }

        public List<CodeInstruction>? TryOverwriteIL(CodeInstruction? code, int? index = null)
        {
            return TryOverwriteIL(codeRange: code != null ? [code] : [], index: index);
        }

        public List<CodeInstruction> OverwriteIL(IEnumerable<CodeInstruction> codeRange, int? index = null, string errorMessage = "Out of bounds!")
        {
            index ??= CurrentIndex;
            return TryOverwriteIL(codeRange, index)
                ?? throw new Exception($"[libs.ILTools.OverwriteIL] [{codeRange}, {index}, {index + codeRange.Count()} ({index} + {codeRange.Count()})] | {errorMessage}");
        }

        public List<CodeInstruction> OverwriteIL(CodeInstruction? code, int? index = null, string errorMessage = "Out of bounds!")
        {
            index ??= CurrentIndex;
            return TryOverwriteIL(codeRange: code != null ? [code] : [], index: index)
                ?? throw new Exception($"[libs.ILTools.OverwriteIL] [{code}, {index}, {index + (code != null ? 1 : 0)} ({index} + {(code != null ? 1 : 0)})] | {errorMessage}");
        }
    }
}