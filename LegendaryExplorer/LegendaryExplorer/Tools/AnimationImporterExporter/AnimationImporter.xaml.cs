﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LegendaryExplorer.Dialogs;
using LegendaryExplorer.SharedUI;
using LegendaryExplorer.SharedUI.Bases;
using LegendaryExplorer.SharedUI.Interfaces;
using LegendaryExplorer.UserControls.SharedToolControls;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;
using LegendaryExplorerCore.Unreal.BinaryConverters;
using Microsoft.AppCenter.Analytics;
using Microsoft.Win32;
using Path = System.IO.Path;

namespace LegendaryExplorer.Tools.AnimationImporterExporter
{
    /// <summary>
    /// Interaction logic for AnimationImporterExporterWindow.xaml
    /// </summary>
    public partial class AnimationImporterExporterWindow : WPFBase, IRecents
    {
        public const string PSAFilter = "*.psa|*.psa";

        public AnimationImporterExporterWindow() : base("Animation Importer/Exporter")
        {
            DataContext = this;
            LoadCommands();
            InitializeComponent();

            RecentsController.InitRecentControl(Toolname, Recents_MenuItem, fileName => LoadFile(fileName));
        }

        public AnimationImporterExporterWindow(ExportEntry exportToLoad) : this()
        {
            FileQueuedForLoad = exportToLoad.FileRef.FilePath;
            ExportQueuedForFocusing = exportToLoad;
        }

        public AnimationImporterExporterWindow(string filePath, int uIndex = 0) : this()
        {
            FileQueuedForLoad = filePath;
            ExportQueuedForFocusing = null;
            UIndexQueuedForFocusing = uIndex;
        }

        private ExportEntry _currentExport;
        public ExportEntry CurrentExport
        {
            get => _currentExport;
            set
            {
                SetProperty(ref _currentExport, value);
                if (value == null)
                {
                    BinaryInterpreterTab_BinaryInterpreter.UnloadExport();
                    InterpreterTab_Interpreter.UnloadExport();

                }
                else
                {
                    BinaryInterpreterTab_BinaryInterpreter.LoadExport(CurrentExport);
                    InterpreterTab_Interpreter.LoadExport(CurrentExport);
                }
            }
        }

        private string FileQueuedForLoad;
        private ExportEntry ExportQueuedForFocusing;
        private readonly int UIndexQueuedForFocusing;

        public ObservableCollectionExtended<ExportEntry> AnimSequenceExports { get; } = new();

        public ICommand OpenFileCommand { get; set; }
        public ICommand SaveFileCommand { get; set; }
        public ICommand SaveAsCommand { get; set; }
        public ICommand ImportFromUDKCommand { get; set; }
        public ICommand ReplaceFromUDKCommand { get; set; }
        public ICommand ImportFromPSACommand { get; set; }
        public ICommand ReplaceFromPSACommand { get; set; }
        public ICommand ExportAnimSeqToPSACommand { get; set; }
        public ICommand ExportAnimSetToPSACommand { get; set; }
        private void LoadCommands()
        {
            OpenFileCommand = new GenericCommand(OpenFile);
            SaveFileCommand = new GenericCommand(SaveFile, IsPackageLoaded);
            SaveAsCommand = new GenericCommand(SaveFileAs, IsPackageLoaded);

            ImportFromUDKCommand = new GenericCommand(ImportFromUDK, IsPackageLoaded);
            ReplaceFromUDKCommand = new GenericCommand(ReplaceFromUDK, IsAnimSequenceSelected);
            ImportFromPSACommand = new GenericCommand(ImportFromPSA, IsPackageLoaded);
            ReplaceFromPSACommand = new GenericCommand(ReplaceFromPSA, IsAnimSequenceSelected);
            ExportAnimSeqToPSACommand = new GenericCommand(ExportAnimSeqToPSA, IsAnimSequenceSelected);
            ExportAnimSetToPSACommand = new GenericCommand(ExportAnimSetToPSA, IsBioAnimDataSelected);
        }

        private void ExportAnimSetToPSA()
        {
            throw new NotImplementedException();
        }

        private void ExportAnimSeqToPSA()
        {
            if (ObjectBinary.From(CurrentExport) is AnimSequence animSequence)
            {
                string sequenceName = CurrentExport.GetProperty<NameProperty>("SequenceName")?.Value ?? CurrentExport.ObjectName;
                var dlg = new SaveFileDialog
                {
                    Filter = PSAFilter,
                    FileName = $"{sequenceName}.psa",
                    AddExtension = true,

                };
                if (dlg.ShowDialog(this) == true)
                {
                    PSA.CreateFrom(animSequence).ToFile(dlg.FileName);
                    MessageBox.Show("Done!", "PSA Export", MessageBoxButton.OK);
                }
            }
        }

        private void ReplaceFromPSA()
        {
            if (CurrentExport.ClassName == "AnimSequence")
            {
                var dlg = new OpenFileDialog
                {
                    Filter = PSAFilter,
                    CheckFileExists = true,
                    Title = "Select PSA",
                    Multiselect = false
                };
                if (dlg.ShowDialog(this) == true)
                {
                    var psa = PSA.FromFile(dlg.FileName);
                    var psaSeqs = psa.GetAnimSequences();
                    if (psaSeqs.IsEmpty())
                    {
                        MessageBox.Show("This PSA is empty!", "", MessageBoxButton.OK, MessageBoxImage.Error); //can this happen?
                        return;
                    }
                    var curSeq = CurrentExport.GetBinaryData<AnimSequence>();
                    if (!curSeq.Bones.SequenceEqual(psaSeqs[0].Bones))
                    {
                        MessageBox.Show("This PSA contains no compatible Animations! Bone names must be identical to replace this animation.",
                                        "", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    AnimSequence selectedAnimSequence = psaSeqs[0];
                    if (psaSeqs.Count > 1)
                    {
                        var seqName = InputComboBoxDialog.GetValue(this, "Select animation from PSA", "Animation Selector", psaSeqs.Select(s => s.Name.Name));
                        if (seqName.IsEmpty())
                        {
                            return;
                        }

                        selectedAnimSequence = psaSeqs.First(s => s.Name == seqName);
                    }

                    var props = CurrentExport.GetProperties();
                    var originalSeqName = props.GetProp<NameProperty>("SequenceName");
                    selectedAnimSequence.UpdateProps(props, CurrentExport.Game);
                    if (originalSeqName != null)
                    {
                        props.AddOrReplaceProp(originalSeqName);
                    }
                    CurrentExport.WriteProperties(props);
                    CurrentExport.WriteBinary(selectedAnimSequence);
                    MessageBox.Show("Done!", "Replace From PSA", MessageBoxButton.OK);
                }
            }
        }

        private void ImportFromPSA()
        {
            var dlg = new OpenFileDialog
            {
                Filter = PSAFilter,
                CheckFileExists = true,
                Title = "Select PSA",
                Multiselect = false
            };
            if (dlg.ShowDialog(this) == true)
            {
                var psa = PSA.FromFile(dlg.FileName);
                List<AnimSequence> psaSeqs = psa.GetAnimSequences();
                if (psaSeqs.IsEmpty())
                {
                    MessageBox.Show("This PSA is empty!", "", MessageBoxButton.OK, MessageBoxImage.Error); //can this happen?
                    return;
                }

                //todo: Make UI for choosing subset of AnimSequences from PSA.

                var pkg = ExportCreator.CreatePackageExport(Pcc, Path.GetFileNameWithoutExtension(dlg.FileName));

                var bioAnimSetData = ExportCreator.CreateExport(Pcc, "BioAnimSetData", "BioAnimSetData", pkg);
                bioAnimSetData.WriteProperty(new ArrayProperty<NameProperty>(psaSeqs[0].Bones.Select(b => new NameProperty(b.Trim().Replace(' ', '-'))), "TrackBoneNames"));

                foreach (AnimSequence seq in psaSeqs)
                {
                    var seqExp = ExportCreator.CreateExport(Pcc, seq.Name, "AnimSequence", pkg);
                    var props = seqExp.GetProperties();
                    seq.UpdateProps(props, Pcc.Game);
                    props.AddOrReplaceProp(new ObjectProperty(bioAnimSetData, "m_pBioAnimSetData"));
                    seqExp.WriteProperties(props);
                    seqExp.WriteBinary(seq);
                }
                MessageBox.Show("Done!", "Import From PSA", MessageBoxButton.OK);
            }
        }

        private void ReplaceFromUDK()
        {
            if (CurrentExport.ClassName == "AnimSequence")
            {
                var dlg = new OpenFileDialog
                {
                    Filter = GameFileFilters.UDKFileFilter,
                    CheckFileExists = true,
                    Title = "Select UDK file",
                    Multiselect = false
                };
                if (dlg.ShowDialog(this) == true)
                {
                    using var upk = MEPackageHandler.OpenUDKPackage(dlg.FileName);
                    var animSets = upk.Exports.Where(exp => exp.ClassName == "AnimSet").ToList();
                    if (animSets.IsEmpty())
                    {
                        MessageBox.Show("This file contains no AnimSets!", "", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    var curSeq = CurrentExport.GetBinaryData<AnimSequence>();
                    animSets = animSets.Where(set => set.GetProperty<ArrayProperty<NameProperty>>("TrackBoneNames").Select(np => $"{np}").SequenceEqual(curSeq.Bones)).ToList();
                    if (animSets.IsEmpty())
                    {
                        MessageBox.Show("This file contains no compatible Animations! TrackBoneNames must be identical to replace this animation.",
                                        "", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    ExportEntry selectedExport = EntrySelector.GetEntry<ExportEntry>(this, upk, "Select an AnimSequence", entry => entry.ClassName == "AnimSequence" && animSets.Contains(entry.Parent));
                    if (selectedExport is null)
                    {
                        return;
                    }
                    AnimSequence selectedAnimSequence = selectedExport.GetBinaryData<AnimSequence>();


                    var props = CurrentExport.GetProperties();
                    var originalSeqName = props.GetProp<NameProperty>("SequenceName");
                    selectedAnimSequence.UpdateProps(props, CurrentExport.Game);
                    if (originalSeqName != null)
                    {
                        props.AddOrReplaceProp(originalSeqName);
                    }
                    CurrentExport.WriteProperties(props);
                    CurrentExport.WriteBinary(selectedAnimSequence);
                    MessageBox.Show("Done!", "Replace From UDK", MessageBoxButton.OK);
                }
            }
        }

        private void ImportFromUDK()
        {
            var dlg = new OpenFileDialog
            {
                Filter = GameFileFilters.UDKFileFilter,
                CheckFileExists = true,
                Title = "Select UDK file",
                Multiselect = false
            };
            if (dlg.ShowDialog(this) == true)
            {
                using var upk = MEPackageHandler.OpenUDKPackage(dlg.FileName);
                var animSets = upk.Exports.Where(exp => exp.ClassName == "AnimSet").ToList();
                if (animSets.IsEmpty())
                {
                    MessageBox.Show("This file contains no AnimSets!", "", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                ExportEntry selectedExport = EntrySelector.GetEntry<ExportEntry>(this, upk, "Select an AnimSequence, or an Animset",
                                                                                 entry => animSets.Contains(entry) || entry.ClassName == "AnimSequence" && animSets.Contains(entry.Parent));


                var selectedAnimSequences = new List<AnimSequence>();
                ExportEntry animSet;
                if (selectedExport?.ClassName == "AnimSequence")
                {
                    selectedAnimSequences.Add(selectedExport.GetBinaryData<AnimSequence>());
                    animSet = (ExportEntry)selectedExport.Parent;
                }
                else if (selectedExport?.ClassName == "AnimSet")
                {
                    animSet = selectedExport;
                    var sequences = animSet.GetProperty<ArrayProperty<ObjectProperty>>("Sequences");
                    if (sequences is null || sequences.IsEmpty())
                    {
                        MessageBox.Show("This AnimSets has no AnimSeqeunces!", "", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    selectedAnimSequences.AddRange(sequences.Select(op => upk.GetUExport(op.Value).GetBinaryData<AnimSequence>()));
                }
                else
                {
                    return;
                }


                var pkg = ExportCreator.CreatePackageExport(Pcc, animSet.ObjectName);

                var bioAnimSetData = ExportCreator.CreateExport(Pcc, animSet.ObjectName, "BioAnimSetData", pkg);
                bioAnimSetData.WriteProperty(animSet.GetProperty<ArrayProperty<NameProperty>>("TrackBoneNames"));

                foreach (AnimSequence seq in selectedAnimSequences)
                {
                    var seqExp = ExportCreator.CreateExport(Pcc, seq.Name, "AnimSequence", pkg);
                    var props = seqExp.GetProperties();
                    seq.UpdateProps(props, Pcc.Game);
                    props.AddOrReplaceProp(new ObjectProperty(bioAnimSetData, "m_pBioAnimSetData"));
                    seqExp.WriteProperties(props);
                    seqExp.WriteBinary(seq);
                }
                MessageBox.Show("Done!", "Import From UDK", MessageBoxButton.OK);
            }
        }

        private bool IsBioAnimDataSelected() => CurrentExport?.ClassName == "BioAnimSetData";

        private bool IsAnimSequenceSelected() => CurrentExport?.ClassName == "AnimSequence";

        private bool IsPackageLoaded() => Pcc != null;

        private async void SaveFile()
        {
            await Pcc.SaveAsync();
        }

        private async void SaveFileAs()
        {
            string fileFilter;
            switch (Pcc.Game)
            {
                case MEGame.ME1:
                    fileFilter = GameFileFilters.ME1SaveFileFilter;
                    break;
                case MEGame.ME2:
                case MEGame.ME3:
                    fileFilter = GameFileFilters.ME3ME2SaveFileFilter;
                    break;
                // TODO: IMPLEMENT IN HEX
                default:
                    string extension = Path.GetExtension(Pcc.FilePath);
                    fileFilter = $"*{extension}|*{extension}";
                    break;
            }
            var d = new SaveFileDialog { Filter = fileFilter };
            if (d.ShowDialog() == true)
            {
                await Pcc.SaveAsync(d.FileName);
                MessageBox.Show("Done");
            }
        }

        private void OpenFile()
        {
            var d = new OpenFileDialog { Filter = GameFileFilters.OpenFileFilter };
            if (d.ShowDialog() == true)
            {
#if !DEBUG
                try
                {
#endif
                LoadFile(d.FileName);
#if !DEBUG
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Unable to open file:\n" + ex.Message);
                }
#endif
            }
        }

        public void LoadFile(string s, int goToIndex = 0)
        {
            try
            {
                //BusyText = "Loading " + Path.GetFileName(s);
                //IsBusy = true;
                StatusBar_LeftMostText.Text = $"Loading {Path.GetFileName(s)} ({FileSize.FormatSize(new FileInfo(s).Length)})";
                Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.ContextIdle, null);
                LoadMEPackage(s);

                AnimSequenceExports.ReplaceAll(Pcc.Exports.Where(exp => exp.ClassName == "AnimSequence"));

                StatusBar_LeftMostText.Text = Path.GetFileName(s);
                Title = $"Animation Importer/Exporter - {s}";

                RecentsController.AddRecent(s, false, Pcc?.Game);
                RecentsController.SaveRecentList(true);
                if (goToIndex != 0)
                {
                    CurrentExport = AnimSequenceExports.FirstOrDefault(x => x.UIndex == goToIndex);
                    ExportQueuedForFocusing = CurrentExport;
                }
            }
            catch (Exception e)
            {
                StatusBar_LeftMostText.Text = "Failed to load " + Path.GetFileName(s);
                MessageBox.Show($"Error loading {Path.GetFileName(s)}:\n{e.Message}");
            }
        }

        public override void handleUpdate(List<PackageUpdate> updates)
        {
            if (CurrentExport != null && updates.Any(update => update.Change == PackageChange.ExportData && update.Index == CurrentExport.UIndex) && CurrentExport.ClassName == "AnimSequence")
            {
                CurrentExport = CurrentExport;//trigger propertyset stuff
            }

            List<PackageUpdate> exportUpdates = updates.Where(upd => upd.Change.HasFlag(PackageChange.Export)).ToList();
            bool shouldUpdateList = false;
            foreach (ExportEntry animSequenceExport in AnimSequenceExports)
            {
                if (exportUpdates.Any(upd => upd.Index == animSequenceExport.UIndex))
                {
                    shouldUpdateList = true;
                    break;
                }
            }

            if (!shouldUpdateList)
            {
                foreach (PackageUpdate update in exportUpdates)
                {
                    if (Pcc.GetEntry(update.Index) is IEntry {ClassName: "AnimSequence"})
                    {
                        shouldUpdateList = true;
                        break;
                    }
                }
            }

            if (shouldUpdateList)
            {
                AnimSequenceExports.ReplaceAll(Pcc.Exports.Where(exp => exp.ClassName == "AnimSequence"));
            }
        }

        private void AnimationImporter_OnLoaded(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(FileQueuedForLoad))
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    //Wait for all children to finish loading
                    LoadFile(FileQueuedForLoad);
                    FileQueuedForLoad = null;

                    if (ExportQueuedForFocusing is null && Pcc.IsUExport(UIndexQueuedForFocusing))
                    {
                        ExportQueuedForFocusing = Pcc.GetUExport(UIndexQueuedForFocusing);
                    }

                    if (AnimSequenceExports.Contains(ExportQueuedForFocusing))
                    {
                        CurrentExport = ExportQueuedForFocusing;
                    }
                    ExportQueuedForFocusing = null;

                    Activate();
                }));
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                // Note that you can have more than one file.
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                string ext = Path.GetExtension(files[0]).ToLower();
                if (ext != ".upk" && ext != ".pcc" && ext != ".sfm")
                {
                    e.Effects = DragDropEffects.None;
                    e.Handled = true;
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
            }
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                // Note that you can have more than one file.
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                string ext = Path.GetExtension(files[0]).ToLower();
                if (ext == ".upk" || ext == ".pcc" || ext == ".sfm")
                {
                    LoadFile(files[0]);
                }
            }
        }
        private void OpenInPackageEditor_Clicked(object sender, RoutedEventArgs e)
        {
            if (AnimExportsListBox.SelectedItem is ExportEntry export)
            {
                var p = new PackageEditor.PackageEditorWindow();
                p.Show();
                p.LoadFile(export.FileRef.FilePath, export.UIndex);
                p.Activate(); //bring to front
            }
        }

        public void PropogateRecentsChange(IEnumerable<RecentsControl.RecentItem> newRecents)
        {
            RecentsController.PropogateRecentsChange(false, newRecents);
        }

        public string Toolname => "AnimationImporterExporter";

        private void AnimationImporter_OnClosing(object sender, CancelEventArgs e)
        {
            InterpreterTab_Interpreter?.Dispose();
            BinaryInterpreterTab_BinaryInterpreter?.Dispose();
        }
    }
}
