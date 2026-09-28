using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using Avalonia.Threading;
using DynamicData.Binding;
using OpenUtau.App.Controls;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.ViewModels;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.SourceGenerators;

namespace OpenUtau.App.ViewModels {
    public class PhonemeMouseoverEvent {
        public readonly UPhoneme? mouseoverPhoneme;
        public PhonemeMouseoverEvent(UPhoneme? mouseoverPhoneme) {
            this.mouseoverPhoneme = mouseoverPhoneme;
        }
    }

    public class NotesContextMenuArgs {
        public PianoRollViewModel? ViewModel { get; set; }

        public bool ForNote { get; set; }
        public NoteHitInfo NoteHitInfo { get; set; }

        public bool ForPitchPoint { get; set; }
        public bool PitchPointIsFirst { get; set; }
        public bool PitchPointCanDel { get; set; }
        public bool PitchPointCanAdd { get; set; }
        public PitchPointHitInfo PitchPointHitInfo { get; set; }
    }

    public class PianorollRefreshEvent {
        public readonly string refreshItem;
        public PianorollRefreshEvent(string refreshItem) {
            this.refreshItem = refreshItem;
        }
    }

    public partial class PianoRollViewModel : ViewModelBase, ICmdSubscriber {

        [Reactive] public partial NotesViewModel NotesViewModel { get; set; }
        [Reactive] public partial PlaybackViewModel? PlaybackViewModel { get; set; }
        [Reactive] public partial CurveViewModel CurveViewModel { get; set; }

        public double Width => Preferences.Default.PianorollWindowSize.Width;
        public double Height => Preferences.Default.PianorollWindowSize.Height;

        public bool LockPitchPoints { get => Preferences.Default.LockUnselectedNotesPitch; }
        public bool LockVibrato { get => Preferences.Default.LockUnselectedNotesVibrato; }
        public bool LockExpressions { get => Preferences.Default.LockUnselectedNotesExpressions; }
        public bool ShowPortrait { get => Preferences.Default.ShowPortrait; }
        public bool ShowIcon { get => Preferences.Default.ShowIcon; }
        public bool ShowGhostNotes { get => Preferences.Default.ShowGhostNotes; }
        public bool UseTrackColor { get => Preferences.Default.UseTrackColor; }
        public bool DegreeStyle0 { get => Preferences.Default.DegreeStyle == 0 ? true : false; }
        public bool DegreeStyle1 { get => Preferences.Default.DegreeStyle == 1 ? true : false; }
        public bool DegreeStyle2 { get => Preferences.Default.DegreeStyle == 2 ? true : false; }
        public bool LockStartTime0 { get => Preferences.Default.LockStartTime == 0 ? true : false; }
        public bool LockStartTime1 { get => Preferences.Default.LockStartTime == 1 ? true : false; }
        public bool LockStartTime2 { get => Preferences.Default.LockStartTime == 2 ? true : false; }
        public bool PlaybackAutoScroll0 { get => Preferences.Default.PlaybackAutoScroll == 0 ? true : false; }
        public bool PlaybackAutoScroll1 { get => Preferences.Default.PlaybackAutoScroll == 1 ? true : false; }
        public bool PlaybackAutoScroll2 { get => Preferences.Default.PlaybackAutoScroll == 2 ? true : false; }
        public bool PianoRollDetached { get => Preferences.Default.DetachPianoRoll; }
        public bool HideMenuItemVisible => !Preferences.Default.DetachPianoRoll;
        public bool ShowPhonemizerTags {
            get => Preferences.Default.ShowPhonemizerTags;
            set {
                Preferences.Default.ShowPhonemizerTags = value;
                Preferences.Save();
                this.RaisePropertyChanged(nameof(ShowPhonemizerTags));
            }
        }

        public EditTool EditTool { get; set; } = Preferences.Default.EditTool;
        [Reactive] public partial int ToolIndex { get; set; } = Preferences.Default.EditTool.BaseTool;
        [Reactive] public partial int PenToolIndex { get; set; } = Preferences.Default.EditTool.PenToolVariation;
        [Reactive] public partial bool PitchOverwrite { get; set; } = Preferences.Default.EditTool.OverwritePitch;

        public ObservableCollectionExtended<MenuItemViewModel> LegacyPlugins { get; private set; }
            = new ObservableCollectionExtended<MenuItemViewModel>();
        public ObservableCollectionExtended<MenuItemViewModel> NoteBatchEdits { get; private set; }
            = new ObservableCollectionExtended<MenuItemViewModel>();
        public ObservableCollectionExtended<MenuItemViewModel> LyricBatchEdits { get; private set; }
            = new ObservableCollectionExtended<MenuItemViewModel>();
        public ObservableCollectionExtended<MenuItemViewModel> ResetBatchEdits { get; private set; }
            = new ObservableCollectionExtended<MenuItemViewModel>();
        public ObservableCollectionExtended<MenuItemViewModel> ExternalBatchEdits { get; private set; }
            = new ObservableCollectionExtended<MenuItemViewModel>();
        public ObservableCollectionExtended<MenuItemViewModel> NotesContextMenuItems { get; private set; }
            = new ObservableCollectionExtended<MenuItemViewModel>();
        public Dictionary<Key, MenuItemViewModel> LegacyPluginShortcuts { get; private set; }
            = new Dictionary<Key, MenuItemViewModel>();

        [Reactive] public partial double Progress { get; set; }
        [Reactive] public partial bool CanUndo { get; set; } = false;
        [Reactive] public partial bool CanRedo { get; set; } = false;
        [Reactive] public partial string UndoText { get; set; } = ThemeManager.GetString("menu.edit.undo");
        [Reactive] public partial string RedoText { get; set; } = ThemeManager.GetString("menu.edit.redo");

        public ReactiveCommand<NoteHitInfo, RxVoid> NoteDeleteCommand { get; set; }
        public ReactiveCommand<NoteHitInfo, RxVoid> NoteCopyCommand { get; set; }
        public ReactiveCommand<NoteHitInfo, RxVoid> ClearPhraseCacheCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, RxVoid> PitEaseInOutCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, RxVoid> PitLinearCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, RxVoid> PitEaseInCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, RxVoid> PitEaseOutCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, RxVoid> PitSplineCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, RxVoid> PitSnapCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, RxVoid> PitDelCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, RxVoid> PitAddCommand { get; set; }

        private ReactiveCommand<Classic.Plugin, RxVoid> legacyPluginCommand;
        /// <summary>Track selected in the tracks panel, or -1 when none/several are selected.</summary>
        private int selectedTrackNo = -1;
        /// <summary>Track of the last part opened in the piano roll.</summary>
        private int lastPartTrackNo = -1;

        public PianoRollViewModel() {
            NotesViewModel = new NotesViewModel();
            CurveViewModel = new CurveViewModel();

            this.WhenAnyValue(vm => vm.ToolIndex)
                .Subscribe(index => EditTool.BaseTool = index);
            this.WhenAnyValue(vm => vm.PenToolIndex)
                .Subscribe(index => EditTool.PenToolVariation = index);
            this.WhenAnyValue(vm => vm.PitchOverwrite)
                .Subscribe(val => { EditTool.OverwritePitch = val; Preferences.Default.EditTool.OverwritePitch = val; Preferences.Save(); });

            NoteDeleteCommand = ReactiveCommand.Create<NoteHitInfo>(info => {
                NotesViewModel.DeleteSelectedNotes();
            });
            NoteCopyCommand = ReactiveCommand.Create<NoteHitInfo>(info => {
                NotesViewModel.CopyNotes();
            });
            ClearPhraseCacheCommand = ReactiveCommand.Create<NoteHitInfo>(info => {
                NotesViewModel.ClearPhraseCache();
            });
            PitEaseInOutCommand = ReactiveCommand.Create<PitchPointHitInfo>(info => {
                if (NotesViewModel.Part == null) { return; }
                DocManager.Inst.StartUndoGroup("command.pitch.editpoint");
                DocManager.Inst.ExecuteCmd(new ChangePitchPointShapeCommand(NotesViewModel.Part, info.Note.pitch.data[info.Index], PitchPointShape.io));
                DocManager.Inst.EndUndoGroup();
            });
            PitLinearCommand = ReactiveCommand.Create<PitchPointHitInfo>(info => {
                if (NotesViewModel.Part == null) { return; }
                DocManager.Inst.StartUndoGroup("command.pitch.editpoint");
                DocManager.Inst.ExecuteCmd(new ChangePitchPointShapeCommand(NotesViewModel.Part, info.Note.pitch.data[info.Index], PitchPointShape.l));
                DocManager.Inst.EndUndoGroup();
            });
            PitEaseInCommand = ReactiveCommand.Create<PitchPointHitInfo>(info => {
                if (NotesViewModel.Part == null) { return; }
                DocManager.Inst.StartUndoGroup("command.pitch.editpoint");
                DocManager.Inst.ExecuteCmd(new ChangePitchPointShapeCommand(NotesViewModel.Part, info.Note.pitch.data[info.Index], PitchPointShape.i));
                DocManager.Inst.EndUndoGroup();
            });
            PitEaseOutCommand = ReactiveCommand.Create<PitchPointHitInfo>(info => {
                if (NotesViewModel.Part == null) { return; }
                DocManager.Inst.StartUndoGroup("command.pitch.editpoint");
                DocManager.Inst.ExecuteCmd(new ChangePitchPointShapeCommand(NotesViewModel.Part, info.Note.pitch.data[info.Index], PitchPointShape.o));
                DocManager.Inst.EndUndoGroup();
            });
            PitSplineCommand = ReactiveCommand.Create<PitchPointHitInfo>(info => {
                if (NotesViewModel.Part == null) { return; }
                DocManager.Inst.StartUndoGroup("command.pitch.editpoint");
                DocManager.Inst.ExecuteCmd(new ChangePitchPointShapeCommand(NotesViewModel.Part, info.Note.pitch.data[info.Index], PitchPointShape.sp));
                DocManager.Inst.EndUndoGroup();
            });
            PitSnapCommand = ReactiveCommand.Create<PitchPointHitInfo>(info => {
                if (NotesViewModel.Part == null) { return; }
                DocManager.Inst.StartUndoGroup("command.pitch.editpoint");
                DocManager.Inst.ExecuteCmd(new SnapPitchPointCommand(NotesViewModel.Part, info.Note));
                DocManager.Inst.EndUndoGroup();
            });
            PitDelCommand = ReactiveCommand.Create<PitchPointHitInfo>(info => {
                if (NotesViewModel.Part == null) { return; }
                DocManager.Inst.StartUndoGroup("command.pitch.delete");
                DocManager.Inst.ExecuteCmd(new DeletePitchPointCommand(NotesViewModel.Part, info.Note, info.Index));
                DocManager.Inst.EndUndoGroup();
            });
            PitAddCommand = ReactiveCommand.Create<PitchPointHitInfo>(info => {
                if (NotesViewModel.Part == null) { return; }
                DocManager.Inst.StartUndoGroup("command.pitch.add");
                DocManager.Inst.ExecuteCmd(new AddPitchPointCommand(NotesViewModel.Part, info.Note, new PitchPoint(info.X, info.Y, NotePresets.Default.DefaultPitchShape), info.Index + 1));
                DocManager.Inst.EndUndoGroup();
            });

            legacyPluginCommand = ReactiveCommand.Create<Classic.Plugin>(async plugin => {
                var project = NotesViewModel.Project;
                var part = NotesViewModel.Part ?? CreateEmptyPartForPlugin(project);
                if (part == null) {
                    return;
                }
                DocManager.Inst.ExecuteCmd(new LoadingNotification(typeof(PianoRoll), true, "legacy plugin"));
                
                try {
                    // Pass the selected notes. Plugins with "notes=all" in plugin.txt, and
                    // plugins run with nothing selected, receive the whole part instead.
                    // An empty track is allowed: the plugin then runs with no notes.
                    UNote? first = null;
                    UNote? last = null;
                    if (!NotesViewModel.Selection.IsEmpty) {
                        first = NotesViewModel.Selection.FirstOrDefault();
                        last = NotesViewModel.Selection.LastOrDefault();
                    }
                    var runner = PluginRunner.from(PathManager.Inst, DocManager.Inst);
                    await runner.Execute(project, part, first, last, plugin);

                } catch (Exception e) {
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
                } finally {
                    DocManager.Inst.ExecuteCmd(new LoadingNotification(typeof(PianoRoll), false, "legacy plugin"));
                }
            });
            LoadLegacyPlugins();
            // Remember which track the piano roll is working on, so a legacy plugin can be
            // run even after the last part of that track is removed (an empty track).
            NotesViewModel.WhenAnyValue(vm => vm.Part)
                .Subscribe(part => {
                    if (part != null) {
                        lastPartTrackNo = part.trackNo;
                    }
                });
            MessageBus.Current.Listen<TrackSelectionEvent>()
                .Subscribe(e => {
                    selectedTrackNo = e.selectedTracks.Length == 1 ? e.selectedTracks[0].TrackNo : -1;
                });
            DocManager.Inst.AddSubscriber(this);
        }

        /// <summary>
        /// Legacy plugins may run on an empty track, which has no part (and no notes) to
        /// write into. Create an empty part on the current track so the plugin has
        /// somewhere to add notes. Returns null when no track can be determined.
        /// </summary>
        private UVoicePart? CreateEmptyPartForPlugin(UProject project) {
            int trackNo = selectedTrackNo >= 0 ? selectedTrackNo : lastPartTrackNo;
            if (trackNo < 0 || trackNo >= project.tracks.Count) {
                return null;
            }
            int position = NotesViewModel.TickOrigin;
            project.timeAxis.TickPosToBarBeat(position, out int bar, out int beat, out int remainingTicks);
            var part = new UVoicePart() {
                trackNo = trackNo,
                position = position,
                Duration = project.timeAxis.BarBeatToTickPos(bar + 4, beat) + remainingTicks - position,
            };
            DocManager.Inst.StartUndoGroup("command.part.add");
            DocManager.Inst.ExecuteCmd(new AddPartCommand(project, part));
            DocManager.Inst.EndUndoGroup();
            DocManager.Inst.ExecuteCmd(new LoadPartNotification(part, project, position));
            return part;
        }

        private void SetUndoState() {
            CanUndo = DocManager.Inst.GetUndoState(out string? undoNameKey);
            if (!string.IsNullOrWhiteSpace(undoNameKey)) {
                UndoText = $"{ThemeManager.GetString("menu.edit.undo")}: {ThemeManager.GetString(undoNameKey)}";
            } else {
                UndoText = ThemeManager.GetString("menu.edit.undo");
            }
            CanRedo = DocManager.Inst.GetRedoState(out string? redoNameKey);
            if (!string.IsNullOrWhiteSpace(redoNameKey)) {
                RedoText = $"{ThemeManager.GetString("menu.edit.redo")}:  {ThemeManager.GetString(redoNameKey)}";
            } else {
                RedoText = ThemeManager.GetString("menu.edit.redo");
            }
        }

        private void LoadLegacyPlugins() {
            LegacyPlugins.Clear();
            LegacyPlugins.AddRange(DocManager.Inst.Plugins.Select(plugin => new MenuItemViewModel() {
                Header = plugin.Name,
                Command = legacyPluginCommand,
                CommandParameter = plugin,
            }));

            LegacyPluginShortcuts.Clear();
            foreach (MenuItemViewModel menu in LegacyPlugins) {
                if (menu.CommandParameter is Classic.Plugin plugin) {
                    if (Enum.TryParse(plugin.Shortcut, out Key key) && !LegacyPluginShortcuts.ContainsKey(key)) {
                        LegacyPluginShortcuts.Add(key, menu);
                    }
                }
            }
            LegacyPlugins.Add(new MenuItemViewModel() { // Separator
                Header = "-",
                Height = 1
            });
            LegacyPlugins.Add(new MenuItemViewModel() {
                Header = ThemeManager.GetString("pianoroll.menu.plugin.openfolder"),
                Command = ReactiveCommand.Create(() => {
                    try {
                        OS.OpenFolder(PathManager.Inst.PluginsPath);
                    } catch (Exception e) {
                        DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
                    }
                })
            });
            LegacyPlugins.Add(new MenuItemViewModel() {
                Header = ThemeManager.GetString("pianoroll.menu.plugin.reload"),
                Command = ReactiveCommand.Create(() => {
                    DocManager.Inst.SearchAllLegacyPlugins();
                    LoadLegacyPlugins();
                })
            });
        }

        public void Undo() => DocManager.Inst.Undo();
        public void Redo() => DocManager.Inst.Redo();
        public void Cut() {
            if (CurveViewModel.IsSelected(NotesViewModel.PrimaryKey)) {
                CurveViewModel.Cut(NotesViewModel.Part!);
            } else {
                NotesViewModel.CutNotes();
            }
        }
        public void Copy() {
            if (CurveViewModel.IsSelected(NotesViewModel.PrimaryKey)) {
                CurveViewModel.Copy(NotesViewModel.Part!);
            } else {
                NotesViewModel.CopyNotes();
            }
        }
        public void Paste() {
            if (DocManager.Inst.NotesClipboard != null && DocManager.Inst.NotesClipboard.Count > 0) {
                NotesViewModel.PasteNotes();
            } else if (DocManager.Inst.CurvesClipboard != null && NotesViewModel.Part != null) {
                var track = NotesViewModel.Project.tracks[NotesViewModel.Part.trackNo];
                if (track.TryGetExpDescriptor(NotesViewModel.Project, NotesViewModel.PrimaryKey, out var descriptor)) {
                    CurveViewModel.Paste(NotesViewModel.Part, descriptor);
                }
            }
        }
        public void PastePlain() => NotesViewModel.PastePlainNotes();
        public void Delete() => NotesViewModel.DeleteSelectedNotes();
        public void SelectAll() => NotesViewModel.SelectAllNotes();

        public void MouseoverPhoneme(UPhoneme? phoneme) {
            MessageBus.Current.SendMessage(new PhonemeMouseoverEvent(phoneme));
        }

        #region ICmdSubscriber

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is ProgressBarNotification progressBarNotification) {
                if (PianoRollDetached) {
                    Dispatcher.UIThread.InvokeAsync(() => {
                        Progress = progressBarNotification.Progress;
                    }, DispatcherPriority.Background);
                }
            }
            SetUndoState();
        }

        #endregion
    }
}