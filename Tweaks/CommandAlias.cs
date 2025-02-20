﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Hooking;
using ImGuiNET;
using SimpleTweaksPlugin.Tweaks;
using SimpleTweaksPlugin.TweakSystem;

namespace SimpleTweaksPlugin {
    public partial class SimpleTweaksPluginConfig {
        public bool ShouldSerializeCommandAlias() => CommandAlias != null;
        public CommandAlias.Config CommandAlias = null;
    }
}

namespace SimpleTweaksPlugin.Tweaks {
    public class CommandAlias : Tweak {
        #region Config
        public class Config : TweakConfig {
            public List<AliasEntry> AliasList = new List<AliasEntry>();
        }
        
        public Config TweakConfig { get; private set; }
        

        public class AliasEntry {
            public static readonly string[] NoOverwrite = { "xlplugins", "xlsettings", "xldclose", "xldev", "tweaks" };
            public bool Enabled = true;
            public string Input = string.Empty;
            public string Output = string.Empty;
            [NonSerialized] public bool Delete = false;
            [NonSerialized] public int UniqueId = 0;
            public bool IsValid() {
                if (NoOverwrite.Contains(Input)) return false;
                return !(string.IsNullOrWhiteSpace(Input) || string.IsNullOrWhiteSpace(Output));
            }

        }

        protected override DrawConfigDelegate DrawConfigTree => (ref bool change) => {
            ImGui.Text("添加命令同义词,不要输入起始的'/'");
            ImGui.Text("这些同义词在设计上不会在宏中生效");
            if (ImGui.IsItemHovered()) {
                ImGui.SetNextWindowSize(new Vector2(280, -1));
                ImGui.BeginTooltip();
                ImGui.TextWrapped("不在宏中支持是为了防止你在上传角色数据时将同义词一并上传\n请在宏中使用原始命令");
                ImGui.EndTooltip();
            }
            ImGui.Separator();
            ImGui.Columns(4);
            var s = ImGui.GetIO().FontGlobalScale;
            ImGui.SetColumnWidth(0, 60 * s );
            ImGui.SetColumnWidth(1, 150 * s );
            ImGui.SetColumnWidth(2, 150 * s );
            ImGui.Text("已启用");
            ImGui.NextColumn();
            ImGui.Text("输入命令");
            ImGui.NextColumn();
            ImGui.Text("输出命令");
            ImGui.NextColumn();
            ImGui.NextColumn();
            ImGui.Separator();
            
            foreach (var aliasEntry in TweakConfig.AliasList) {

                if (aliasEntry.UniqueId == 0) {
                    aliasEntry.UniqueId = TweakConfig.AliasList.Max(a => a.UniqueId) + 1;
                }

                var focused = false;
                ImGui.Separator();
                if (aliasEntry.IsValid()) {
                    change = ImGui.Checkbox($"###aliasToggle{aliasEntry.UniqueId}", ref aliasEntry.Enabled) || change;
                } else {
                    ImGui.Text("非法");
                }
                ImGui.NextColumn();
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
                ImGui.Text("/");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(-5);
                change |= ImGui.InputText($"###aliasInput{aliasEntry.UniqueId}", ref aliasEntry.Input, 500) || change;
                focused = ImGui.IsItemFocused();
                ImGui.PopStyleVar();
                ImGui.NextColumn();
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
                ImGui.Text("/");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(-5);
                change |= ImGui.InputText($"###aliasOutput{aliasEntry.UniqueId}", ref aliasEntry.Output, 500) || change;
                focused = focused || ImGui.IsItemFocused();
                ImGui.PopStyleVar();
                ImGui.NextColumn();
                
                if (AliasEntry.NoOverwrite.Contains(aliasEntry.Input)) {

                    ImGui.TextColored(new Vector4(1, 0, 0, 1), $"'/{aliasEntry.Input}'是一个被保护的命令");
                } else if (string.IsNullOrEmpty(aliasEntry.Input)) {
                    ImGui.TextColored(new Vector4(1, 0, 0, 1), "输入不可为空");
                } else if (string.IsNullOrEmpty(aliasEntry.Output)) {
                    ImGui.TextColored(new Vector4(1, 0, 0, 1), "输出不可为空");
                } else if (aliasEntry.Input.StartsWith("/")) {
                    ImGui.TextColored(new Vector4(1, 1, 0, 1), "不要输入'/'");
                }

                ImGui.NextColumn();

                if (string.IsNullOrWhiteSpace(aliasEntry.Input) && string.IsNullOrWhiteSpace(aliasEntry.Output)) {
                    aliasEntry.Delete = true;
                }
            }

            if (TweakConfig.AliasList.Count > 0 && TweakConfig.AliasList.RemoveAll(a => a.Delete) > 0) {
                change = true;
            }

            ImGui.Separator();
            var addNew = false;
            var newEntry = new AliasEntry() { UniqueId = TweakConfig.AliasList.Count == 0 ? 1 : TweakConfig.AliasList.Max(a => a.UniqueId) + 1 };
            ImGui.Text("New:");
            ImGui.NextColumn();
            ImGui.SetNextItemWidth(-1);
            addNew = ImGui.InputText($"###aliasInput{newEntry.UniqueId}", ref newEntry.Input, 500) || addNew;
            ImGui.NextColumn();
            ImGui.SetNextItemWidth(-1);
            addNew = ImGui.InputText($"###aliasOutput{newEntry.UniqueId}", ref newEntry.Output, 500) || addNew;
            ImGui.NextColumn();

            if (addNew) {
                TweakConfig.AliasList.Add(newEntry);
                change = true;
            }
            
            ImGui.Columns(1);
        };
        #endregion

        public override string Name => "自定义同义命令";
        public override string Description => "创建聊天栏命令的别称以方便输入";

        private IntPtr processChatInputAddress;
        private unsafe delegate byte ProcessChatInputDelegate(IntPtr uiModule, byte** a2, IntPtr a3);

        private Hook<ProcessChatInputDelegate> processChatInputHook;

        public override void Setup() {
            if (Ready) return;
            try {
                processChatInputAddress = PluginInterface.TargetModuleScanner.ScanText("E8 ?? ?? ?? ?? FE 86 ?? ?? ?? ?? C7 86 ?? ?? ?? ?? ?? ?? ?? ??");
                Ready = true;
            } catch {
                SimpleLog.Log("Failed to find address for ProcessChatInput");
            }
        }

        public override unsafe void Enable() {
            if (!Ready) return;
            TweakConfig = LoadConfig<Config>() ?? PluginConfig.CommandAlias ?? new Config();
            processChatInputHook ??= new Hook<ProcessChatInputDelegate>(processChatInputAddress, new ProcessChatInputDelegate(ProcessChatInputDetour));
            processChatInputHook?.Enable();
            Enabled = true;
        }

        private unsafe byte ProcessChatInputDetour(IntPtr uiModule, byte** message, IntPtr a3) {
            try {
                var bc = 0;
                for (var i = 0; i <= 500; i++) {
                    if (*(*message + i) != 0) continue;
                    bc = i;
                    break;
                }
                if (bc < 2 || bc > 500) {
                    return processChatInputHook.Original(uiModule, message, a3);
                }
                
                var inputString = Encoding.UTF8.GetString(*message, bc);
                if (inputString.StartsWith("/")) {
                    var splitString = inputString.Split(' ');

                    if (splitString.Length > 0 && splitString[0].Length >= 2) {
                        var alias = TweakConfig.AliasList.FirstOrDefault(a => {
                            if (!a.Enabled) return false;
                            if (!a.IsValid()) return false;
                            return splitString[0] == $"/{a.Input}";
                        });
                        if (alias != null) {
                            // https://git.sr.ht/~jkcclemens/CCMM/tree/master/Custom%20Commands%20and%20Macro%20Macros/GameFunctions.cs#L44
                            var newStr = $"/{alias.Output}{inputString.Substring(alias.Input.Length + 1)}";
                            if (newStr.Length <= 500) {
                                SimpleLog.Log($"Aliasing Command: {inputString} -> {newStr}");
                                var bytes = Encoding.UTF8.GetBytes(newStr);
                                var mem1 = Marshal.AllocHGlobal(400);
                                var mem2 = Marshal.AllocHGlobal(bytes.Length + 30);
                                Marshal.Copy(bytes, 0, mem2, bytes.Length);
                                Marshal.WriteByte(mem2 + bytes.Length, 0);
                                Marshal.WriteInt64(mem1, mem2.ToInt64());
                                Marshal.WriteInt64(mem1 + 8, 64);
                                Marshal.WriteInt64(mem1 + 8 + 8, bytes.Length + 1);
                                Marshal.WriteInt64(mem1 + 8 + 8 + 8, 0);
                                var r = processChatInputHook.Original(uiModule, (byte**) mem1.ToPointer(), a3);
                                Marshal.FreeHGlobal(mem1);
                                Marshal.FreeHGlobal(mem2);
                                return r;
                            }
                            SimpleLog.Log($"String longer than 500");
                            PluginInterface.Framework.Gui.Chat.PrintError("[Simple Tweaks] 长度超过500字符，该命令不会被执行");
                            return 0;
                        }
                    }
                }
            } catch (Exception ex) {
                Plugin.Error(this, ex);
            }
            
            return processChatInputHook.Original(uiModule, message, a3);
        }
        
        public override void Disable() {
            SaveConfig(TweakConfig);
            PluginConfig.CommandAlias = null;
            processChatInputHook?.Disable();
            Enabled = false;
        }

        public override void Dispose() {
            if (!Ready) return;
            processChatInputHook?.Disable();
            processChatInputHook?.Dispose();
            Ready = false;
            Enabled = false;
        }
    }
}
