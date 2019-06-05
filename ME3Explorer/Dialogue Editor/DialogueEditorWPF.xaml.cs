﻿using Gammtek.Conduit.Extensions.Collections.Generic;
using ME3Explorer;
using ME3Explorer.Packages;
using ME3Explorer.SequenceObjects;
using ME3Explorer.SharedUI;
using ME3Explorer.SharedUI.Interfaces;
using ME3Explorer.SharedUI.PeregrineTreeView;
using ME3Explorer.Unreal;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition.Primitives;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Gammtek.Conduit.Extensions;
using UMD.HCIL.Piccolo;
using UMD.HCIL.Piccolo.Event;
using UMD.HCIL.Piccolo.Nodes;
using Color = System.Drawing.Color;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using InterpEditor = ME3Explorer.Matinee.InterpEditor;
using static ME3Explorer.TlkManagerNS.TLKManagerWPF;
using System.Windows.Threading;
using Gammtek.Conduit.MassEffect3.SFXGame.StateEventMap;
using KFreonLib.MEDirectories;
using MassEffect.NativesEditor.Views;
using ME3Explorer.Sequence_Editor;
using static ME3Explorer.Dialogue_Editor.BioConversationExtended;

namespace ME3Explorer.Dialogue_Editor
{
    /// <summary>
    /// Interaction logic for DialogueEditorWPF.xaml
    /// </summary>
    public partial class DialogueEditorWPF : WPFBase
    {
        #region Declarations
        private struct SaveData
        {
            public bool absoluteIndex;
            public int index;
            public float X;
            public float Y;

            public SaveData(int i) : this()
            {
                index = i;
            }
        }

        private const float CLONED_SEQREF_MAGIC = 2.237777E-35f;

        private readonly ConvGraphEditor graphEditor;
        public ObservableCollectionExtended<IEntry> FFXAnimsets { get; } = new ObservableCollectionExtended<IEntry>();
        public ObservableCollectionExtended<ConversationExtended> Conversations { get; } = new ObservableCollectionExtended<ConversationExtended>();
        public PropertyCollection CurrentConvoProperties;
        public IExportEntry CurrentLoadedExport;
        public IMEPackage CurrentConvoPackage;
        public ObservableCollectionExtended<SpeakerExtended> SelectedSpeakerList { get; } = new ObservableCollectionExtended<SpeakerExtended>();
        private DialogueNodeExtended _SelectedDialogueNode;
        public DialogueNodeExtended SelectedDialogueNode
        {
            get => _SelectedDialogueNode;
            set => SetProperty(ref _SelectedDialogueNode, value);
        }
       
        //SPEAKERS
        public SpeakerExtended SelectedSpeaker { get; } = new SpeakerExtended(-3, "None", null, null, 0, "Not found"); //Link to speakers box

        #region ConvoBox //Conversation Box Links
        private ConversationExtended _SelectedConv;
        public ConversationExtended SelectedConv
        {
            get => _SelectedConv;
            set => SetProperty(ref _SelectedConv, value);
        }

        private string _level;
        public string Level
        {
            get => _level;
            set => SetProperty(ref _level, value);
        }

        #endregion ConvoBox//Conversation Box Links
        private static BackgroundWorker BackParser = new BackgroundWorker();

        // FOR GRAPHING
        public ObservableCollectionExtended<DObj> CurrentObjects { get; } = new ObservableCollectionExtended<DObj>();
        public ObservableCollectionExtended<DObj> SelectedObjects { get; } = new ObservableCollectionExtended<DObj>();
  
        public string CurrentFile;
        public string JSONpath;
        private List<SaveData> SavedPositions;
        public bool RefOrRefChild;

        public static readonly string DialogueEditorDataFolder = Path.Combine(App.AppDataFolder, @"DialogueEditor\");

        private string _statusText;
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, $"{CurrentFile} {value}");
        }

        public float StartPoDStarts;
        public float StartPoDiagNodes;
        public float StartPoDReplyNodes;

        public ICommand OpenCommand { get; set; }
        public ICommand SaveCommand { get; set; }
        public ICommand SaveAsCommand { get; set; }
        public ICommand SaveImageCommand { get; set; }
        public ICommand SaveViewCommand { get; set; }
        public ICommand AutoLayoutCommand { get; set; }
        public ICommand LoadTLKManagerCommand { get; set; }
        public ICommand OpenInCommand { get; set; }
        public ICommand OpenInCommand_Wwbank { get; set; }
        public ICommand OpenInCommand_FFXNS { get; set; }
        public ICommand SpeakerMoveUpCommand { get; set; }
        public ICommand SpeakerMoveDownCommand { get; set; }
        public ICommand AddSpeakerCommand { get; set; }
        public ICommand DeleteSpeakerCommand { get; set; }
        public ICommand ChangeNameCommand { get; set; }
        private bool HasWwbank(object param)
        {
            return SelectedConv != null && SelectedConv.WwiseBank != null;
        }

        private bool HasFFXNS(object param)
        {
            return SelectedConv != null && SelectedConv.NonSpkrFFX != null;
        }
        private bool SpkrCanMoveUp(object param)
        {
            return SelectedSpeakerList != null && SelectedSpeaker.SpeakerID > 0;
        }
        private bool SpkrCanMoveDown(object param)
        {
            return SelectedSpeakerList != null && SelectedSpeaker.SpeakerID >= 0;
        }
        private bool HasActiveSpkr()
        {
            return Speakers_ListBox.SelectedIndex >= 2;
        }
        #endregion Declarations

        #region Startup/Exit
        public DialogueEditorWPF()
        {
            ME3ExpMemoryAnalyzer.MemoryAnalyzer.AddTrackedMemoryItem("Dialogue Editor WPF", new WeakReference(this));
            LoadCommands();
            StatusText = "Select package file to load";
            InitializeComponent();

            LoadRecentList();

            graphEditor = (ConvGraphEditor)GraphHost.Child;
            graphEditor.BackColor = System.Drawing.Color.FromArgb(130, 130, 130);
            graphEditor.Camera.MouseDown += backMouseDown_Handler;
            graphEditor.Camera.MouseUp += back_MouseUp;

            this.graphEditor.Click += graphEditor_Click;
            this.graphEditor.DragDrop += SequenceEditor_DragDrop;
            this.graphEditor.DragEnter += SequenceEditor_DragEnter;

        }

        public DialogueEditorWPF(IExportEntry export) : this()
        {
            FileQueuedForLoad = export.FileRef.FileName;
            ExportQueuedForFocusing = export;
        }

        private void LoadCommands()
        {
            OpenCommand = new GenericCommand(OpenPackage);
            SaveCommand = new GenericCommand(SavePackage, PackageIsLoaded);
            SaveAsCommand = new GenericCommand(SavePackageAs, PackageIsLoaded);
            SaveImageCommand = new GenericCommand(SaveImage, CurrentObjects.Any);
            AutoLayoutCommand = new GenericCommand(AutoLayout, CurrentObjects.Any);
            LoadTLKManagerCommand = new GenericCommand(LoadTLKManager);
            OpenInCommand = new RelayCommand(OpenInAction);
            OpenInCommand_FFXNS = new RelayCommand(OpenInAction, HasFFXNS);
            OpenInCommand_Wwbank = new RelayCommand(OpenInAction, HasWwbank);
            SpeakerMoveUpCommand = new RelayCommand(SpeakerMoveAction, SpkrCanMoveUp);
            SpeakerMoveDownCommand = new RelayCommand(SpeakerMoveAction, SpkrCanMoveDown);
            AddSpeakerCommand = new GenericCommand(SpeakerAdd);
            DeleteSpeakerCommand = new GenericCommand(SpeakerDelete, HasActiveSpkr);
            ChangeNameCommand = new GenericCommand(SpeakerGoToName, HasActiveSpkr);
        }


        private void DialogueEditorWPF_Loaded(object sender, RoutedEventArgs e)
        {
            if (FileQueuedForLoad != null)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    //Wait for all children to finish loading
                    LoadFile(FileQueuedForLoad);
                    FileQueuedForLoad = null;

                    if (ExportQueuedForFocusing != null)
                    {
                        //GoToExport(ExportQueuedForFocusing);
                        ExportQueuedForFocusing = null;
                    }

                    Activate();
                }));
            }
        }

        private void SavePackageAs()
        {
            string extension = Path.GetExtension(Pcc.FileName);
            SaveFileDialog d = new SaveFileDialog { Filter = $"*{extension}|*{extension}" };
            if (d.ShowDialog() == true)
            {
                Pcc.save(d.FileName);
                MessageBox.Show("Done.");
            }
        }

        private void SavePackage()
        {
            Pcc.save();
        }

        private void DialogueEditorWPF_Closing(object sender, CancelEventArgs e)
        {
            if (e.Cancel)
                return;

            if (AutoSaveView_MenuItem.IsChecked)
                saveView();

            var options = new Dictionary<string, object>
            {
                {"OutputNumbers", DObj.OutputNumbers},
                {"AutoSave", AutoSaveView_MenuItem.IsChecked},

            };
            string outputFile = JsonConvert.SerializeObject(options);
            if (!Directory.Exists(DialogueEditorDataFolder))
                Directory.CreateDirectory(DialogueEditorDataFolder);


            //Code here remove these objects from leaking the window memory
            graphEditor.Camera.MouseDown -= backMouseDown_Handler;
            graphEditor.Camera.MouseUp -= back_MouseUp;
            graphEditor.Click -= graphEditor_Click;
            graphEditor.DragDrop -= SequenceEditor_DragDrop;
            graphEditor.DragEnter -= SequenceEditor_DragEnter;
            CurrentObjects.ForEach(x =>
            {
                x.MouseDown -= node_MouseDown;
                x.Click -= node_Click;
                x.Dispose();
            });
            CurrentObjects.Clear();
            graphEditor.Dispose();
            Properties_InterpreterWPF.Dispose();
            GraphHost.Child = null; //This seems to be required to clear OnChildGotFocus handler from WinFormsHost
            GraphHost.Dispose();
            DataContext = null;
            DispatcherHelper.EmptyQueue();
        }

        private void OpenPackage()
        {
            OpenFileDialog d = new OpenFileDialog { Filter = App.FileFilter };
            if (d.ShowDialog() == true)
            {
                try
                {
                    LoadFile(d.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Unable to open file:\n" + ex.Message);
                }
            }
        }

        private bool PackageIsLoaded()
        {
            System.Diagnostics.Debug.WriteLine("Package Is Loaded.");
            return Pcc != null;
        }

        public void LoadFile(string fileName)
        {
            try
            {
                Conversations.ClearEx();
                SelectedSpeakerList.ClearEx();
                SelectedObjects.ClearEx();

                LoadMEPackage(fileName);
                CurrentFile = System.IO.Path.GetFileName(fileName);
                LoadConversations();
                if (Conversations.IsEmpty())
                {
                    UnloadFile();
                    MessageBox.Show("This file does not contain any Conversations!");
                    return;
                }

                CurrentConvoPackage = Pcc;
                FirstParse();
                RightBarColumn.Width = new GridLength(260);
                graphEditor.nodeLayer.RemoveAllChildren();
                graphEditor.edgeLayer.RemoveAllChildren();

                AddRecent(fileName, false);
                SaveRecentList();
                RefreshRecent(true, RFiles);

                Title = $"Dialogue Editor WPF - {fileName}";
                StatusText = null; 

                Level = Path.GetFileName(Pcc.FileName);
                if (Pcc.Game != MEGame.ME1)
                {
                    Level = $"{Level.Remove(Level.Length - 12)}.pcc";
                }
                else
                {
                    Level = $"{Level.Remove(Level.Length - 4)}_LOC_INT{Path.GetExtension(Pcc.FileName)}";
                }

                //Build Animset list //DEBUG 
                //Must be done on this thread not on Background worker....
                FFXAnimsets.ClearEx();
                foreach (var exp in Pcc.Exports.Where(exp => exp.ClassName == "FaceFXAnimSet"))
                {
                    FFXAnimsets.Add(exp);
                }

            }
            catch (Exception ex)
            {
                MessageBox.Show("Error:\n" + ex.Message);
                Title = "Dialogue Editor WPF";
                UnloadFile();
            }
        }

        private void UnloadFile()
        {

            RightBarColumn.Width = new GridLength(0);
            SelectedConv = null;
            CurrentLoadedExport = null;
            CurrentConvoPackage = null;
            Conversations.ClearEx();
            SelectedSpeakerList.ClearEx();
            Properties_InterpreterWPF.UnloadExport();
            CurrentObjects.Clear();
            graphEditor.nodeLayer.RemoveAllChildren();
            graphEditor.edgeLayer.RemoveAllChildren();
            CurrentFile = null;
            UnLoadMEPackage();
            StatusText = "Select a package file to load";
        }

        public static void PushConvoToFile(ConversationExtended convo)
        {
            
            convo.Export.WriteProperties(convo.BioConvo);

        }
        #endregion Startup/Exit

        #region Parsing
        private void LoadConversations()
        {
            Conversations.ClearEx();
            foreach (var exp in Pcc.Exports.Where(exp => exp.ClassName.Equals("BioConversation")))
            {
                Conversations.Add(new ConversationExtended(exp.UIndex, exp.ObjectName, exp.GetProperties(), exp, new ObservableCollectionExtended<SpeakerExtended>(), new ObservableCollectionExtended<DialogueNodeExtended>(), new ObservableCollectionExtended<DialogueNodeExtended>()));
            }
        }

        private void FirstParse()
        {
            Conversations_ListBox.IsEnabled = false;
            if (SelectedConv != null && SelectedConv.IsFirstParsed == false) //Get Active setup pronto.
            {
                ParseSpeakers(SelectedConv);
                GenerateSpeakerList();
                ParseEntryList(SelectedConv);
                ParseReplyList(SelectedConv);
                ParseNSFFX(SelectedConv);
                ParseSequence(SelectedConv);
                ParseWwiseBank(SelectedConv);
                SelectedConv.IsFirstParsed = true;
            }

            foreach (var conv in Conversations.Where(conv => conv.IsFirstParsed == false)) //Get Speakers entry and replies plus convo data first
            {
                ParseSpeakers(conv);
                ParseEntryList(conv);
                ParseReplyList(conv);
                ParseNSFFX(conv);
                ParseSequence(conv);
                ParseWwiseBank(conv);

                conv.IsFirstParsed = true;
            }



            //Do minor stuff
            foreach (var conv in Conversations.Where(conv => conv.IsParsed == false))
            {
                foreach (var spkr in conv.Speakers)
                {
                    spkr.FaceFX_Male = GetFaceFX(conv, spkr.SpeakerID, true);
                    spkr.FaceFX_Female = GetFaceFX(conv, spkr.SpeakerID, false);
                }
                GenerateSpeakerTags(conv);
                ParseLinesInterpData(conv);
                ParseLinesFaceFX(conv);
                ParseLinesAudioStreams(conv);
                //TO DO:
                //PARSE NODE INTERPDATA, FACEFX, AUDIO WWISESTREAM.
                conv.IsParsed = true;
            }


            BackParser = new BackgroundWorker()
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };
            BackParser.DoWork += BackParse;
            BackParser.RunWorkerCompleted += BackParser_RunWorkerCompleted;
            BackParser.RunWorkerAsync();
        }

        private void BackParse(object sender, DoWorkEventArgs e)
        {

            //TOO MANY PROBLEMS ON BACK THREAD. OPTIMISE LATER.

        }

        private void BackParser_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            BackParser.CancelAsync();
            Conversations_ListBox.IsEnabled = true;

        }

        public void ParseSpeakers(ConversationExtended conv)
        {
            conv.Speakers = new ObservableCollectionExtended<SpeakerExtended>();
            conv.Speakers.Add(new SpeakerExtended(-2, "player", null, null, 125303, "Shepard"));
            conv.Speakers.Add(new SpeakerExtended(-1, "owner", null, null, 0, "No data"));
            if (CurrentConvoPackage.Game != MEGame.ME3)
            {
                var s_speakers = conv.BioConvo.GetProp<ArrayProperty<StructProperty>>("m_SpeakerList");
                if (s_speakers != null)
                {
                    for (int id = 0; id < s_speakers.Count; id++)
                    {
                        var spkr = new SpeakerExtended(id, s_speakers[id].GetProp<NameProperty>("sSpeakerTag").ToString());
                        conv.Speakers.Add(spkr);
                    }
                }
            }
            else
            {
                var a_speakers = conv.BioConvo.GetProp<ArrayProperty<NameProperty>>("m_aSpeakerList");
                if (a_speakers != null)
                {
                    int id = 0;
                    foreach (NameProperty n in a_speakers)
                    {
                        var spkr = new SpeakerExtended(id, n.ToString());
                        conv.Speakers.Add(spkr);
                        id++;
                    }
                }
            }
        }

        public void ParseEntryList(ConversationExtended conv)
        {
            conv.EntryList = new ObservableCollectionExtended<DialogueNodeExtended>();
            try
            {
                var entryprop = conv.BioConvo.GetProp<ArrayProperty<StructProperty>>("m_EntryList"); //ME3/ME1
                int cnt = 0;
                foreach (StructProperty Node in entryprop)
                {
                    int speakerindex = Node.GetProp<IntProperty>("nSpeakerIndex");
                    int linestrref = Node.GetProp<StringRefProperty>("srText").Value;
                    string line = GlobalFindStrRefbyID(linestrref, CurrentConvoPackage.Game);
                    int cond = Node.GetProp<IntProperty>("nConditionalFunc").Value;
                    int stevent = Node.GetProp<IntProperty>("nStateTransition").Value;
                    bool bcond = Node.GetProp<BoolProperty>("bFireConditional");
                    conv.EntryList.Add(new DialogueNodeExtended(Node, false, cnt, speakerindex, linestrref, line, bcond, cond, stevent));
                    cnt++;
                }
            }
            catch (Exception e)
            {
                //throw new Exception("Entry List Parse failed", e);
            }
        }

        public void ParseReplyList(ConversationExtended conv)
        {
            conv.ReplyList = new ObservableCollectionExtended<DialogueNodeExtended>();
            try
            {
                var replyprop = conv.BioConvo.GetProp<ArrayProperty<StructProperty>>("m_ReplyList"); //ME3
                int cnt = 0;
                foreach (StructProperty Node in replyprop)
                {
                    int speakerindex = -2;
                    int linestrref = Node.GetProp<StringRefProperty>("srText").Value;
                    string line = GlobalFindStrRefbyID(linestrref, CurrentConvoPackage.Game);
                    int cond = Node.GetProp<IntProperty>("nConditionalFunc").Value;
                    int stevent = Node.GetProp<IntProperty>("nStateTransition").Value;
                    bool bcond = Node.GetProp<BoolProperty>("bFireConditional");
                    conv.ReplyList.Add(new DialogueNodeExtended(Node, true, cnt, speakerindex, linestrref, line, bcond, cond, stevent));
                    cnt++;
                }
            }
            catch(Exception e)
            {
                //throw new Exception("Reply List Parse failed", e);  //Note some convos don't have replies.
            }
        }

        private void GenerateSpeakerList()
        {
            SelectedSpeakerList.ClearEx();

            foreach (var spkr in SelectedConv.Speakers)
            {
                SelectedSpeakerList.Add(spkr);
            }
        }
        private void GenerateSpeakerTags(ConversationExtended conv)
        {
            var spkrlist = conv.Speakers;
            foreach(var e in conv.EntryList)
            {
                int spkridx = e.SpeakerIndex;
                var spkrtag = conv.Speakers.Where(s => s.SpeakerID == spkridx).FirstOrDefault();
                if (spkrtag != null)
                    e.SpeakerTag = spkrtag.SpeakerName;
            }

            foreach (var r in conv.ReplyList)
            {
                int spkridx = r.SpeakerIndex;
                var spkrtag = conv.Speakers.Where(s => s.SpeakerID == spkridx).FirstOrDefault();
                if (spkrtag != null)
                    r.SpeakerTag = spkrtag.SpeakerName;
            }
        }
        private void ParseLinesInterpData(ConversationExtended conv)
        {
            //TO DO
        }

        private void ParseLinesAudioStreams(ConversationExtended conv)
        {
            //TO DO
        }

        private void ParseLinesFaceFX(ConversationExtended conv)
        {
            //TO DO
        }

        /// <summary>
        /// Returns the IEntry of FaceFXAnimSet
        /// </summary>
        /// <param name="speakerID">SpeakerID -1 = Owner, -2 = Player</param>
        /// <param name="isMale">will pull female by default</param>
        public IEntry GetFaceFX(ConversationExtended conv, int speakerID, bool isMale = false)
        {
            string ffxPropName = "m_aFemaleFaceSets"; //ME2/M£3
            if (isMale)
            {
                ffxPropName = "m_aMaleFaceSets";
            }
            var ffxList = conv.BioConvo.GetProp<ArrayProperty<ObjectProperty>>(ffxPropName);
            if (ffxList != null && ffxList.Count > speakerID + 2)
            {
                return Pcc.getEntry(ffxList[speakerID + 2].Value); 
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Sets the Uindex of appropriate sequence
        /// </summary>
        public void ParseSequence(ConversationExtended conv)
        {
            string propname = "MatineeSequence";
            if (CurrentConvoPackage.Game == MEGame.ME1)
            {
                propname = "m_pEvtSystemSeq";
            }

            var seq = conv.BioConvo.GetProp<ObjectProperty>(propname);
            if (seq != null)
            {
                conv.Sequence = Pcc.getUExport(seq.Value);
            }
            else
            {
                conv.Sequence = null;
            }
        }
        /// <summary>
        /// Sets the Uindex of NonSpeaker FaceFX
        /// </summary>
        public void ParseNSFFX(ConversationExtended conv)
        {
            string propname = "m_pNonSpeakerFaceFXSet";
            if (CurrentConvoPackage.Game == MEGame.ME1)
            {
                propname = "m_pConvFaceFXSet";
            }

            var seq = conv.BioConvo.GetProp<ObjectProperty>(propname);
            if (seq != null)
            {
                conv.NonSpkrFFX = Pcc.getEntry(seq.Value);
            }
            else
            {
                conv.NonSpkrFFX = null;
            }
        }
        /// <summary>
        /// Sets the Uindex of WwiseBank
        /// </summary>
        public void ParseWwiseBank(ConversationExtended conv)
        {
            conv.WwiseBank = null;
            try
            {
                IEntry ffxo = GetFaceFX(conv, -1, true); //find owner animset
                if (!Pcc.isUExport(ffxo.UIndex))
                    return;
                IExportEntry ffxoExport = ffxo as IExportEntry;

                var wwevents = ffxoExport.GetProperty<ArrayProperty<ObjectProperty>>("ReferencedSoundCues"); //pull a wwiseevent array
                if (wwevents == null)
                {
                    IEntry ffxp = GetFaceFX(conv, -2, true); //find player as alternative
                    if (!Pcc.isUExport(ffxo.UIndex))
                        return;
                    IExportEntry ffxpExport = ffxp as IExportEntry;
                    wwevents = ffxpExport.GetProperty<ArrayProperty<ObjectProperty>>("ReferencedSoundCues");
                }

                if (Pcc.Game == MEGame.ME3)
                {
                    StructProperty r = CurrentConvoPackage.getUExport(wwevents[0].Value).GetProperty<StructProperty>("Relationships"); //lookup bank
                    var bank = r.GetProp<ObjectProperty>("Bank");
                    conv.WwiseBank = Pcc.getUExport(bank.Value);
                }
                else if(Pcc.Game == MEGame.ME2) //Game is ME2.  Wwisebank ref in Binary.
                {
                    var data = Pcc.getUExport(wwevents[0].Value).getBinaryData();
                    int binarypos = 4;
                    int count = BitConverter.ToInt32(data, binarypos);
                    if (count > 0)
                    {
                        binarypos += 4;
                        int bnkcount = BitConverter.ToInt32(data, binarypos);
                        if (bnkcount > 0)
                        {
                            binarypos += 4;
                            int bank = BitConverter.ToInt32(data, binarypos);
                            conv.WwiseBank = Pcc.getUExport(bank);
                        }
                    }
                }
            }
            catch
            {
                //ignore
            }
        }

        public int ParseActorsNames(ConversationExtended conv, string tag)
        {
            if (CurrentConvoPackage.Game == MEGame.ME1)
            {
                try
                {
                    var actors = CurrentConvoPackage.Exports.Where(xp => xp.ClassName == "BioPawn");
                    IExportEntry actor = actors.FirstOrDefault(a => a.GetProperty<NameProperty>("Tag").ToString() == tag);
                    var behav = actor.GetProperty<ObjectProperty>("m_oBehavior");
                    var set = CurrentConvoPackage.getUExport(behav.Value).GetProperty<ObjectProperty>("m_oActorType");
                    var strrefprop = CurrentConvoPackage.getUExport(set.Value).GetProperty<StringRefProperty>("ActorGameNameStrRef");
                    if (strrefprop != null)
                    {
                        return strrefprop.Value;
                    }
                }
                catch
                {
                    return -2;
                }
            }

            // ME2/ME3 need to load non-LOC file.  Or parse a JSON.

            return 0;
        }

        public List<int> GetStartingList()
        {
            List<int> startList = new List<int>();
            var prop = CurrentLoadedExport.GetProperty<ArrayProperty<IntProperty>>("m_StartingList"); //ME1/ME2/ME3
            if (prop != null)
            {
                foreach (var sl in prop)
                {
                    startList.Add(sl.Value);
                }

            }
            return startList;
        }

        public void SaveSpeakersToProperties(ObservableCollectionExtended<SpeakerExtended> speakerCollection)
        {
            try
            {

                var m_aSpeakerList = new ArrayProperty<NameProperty>(ArrayType.Name, "m_aSpeakerList");
                var m_SpeakerList = new ArrayProperty<StructProperty>(ArrayType.Struct, "m_SpeakerList");
                var m_aMaleFaceSets = new ArrayProperty<ObjectProperty>(ArrayType.Object, "m_aMaleFaceSets");
                var m_aFemaleFaceSets = new ArrayProperty<ObjectProperty>(ArrayType.Object, "m_aFemaleFaceSets");

                foreach (SpeakerExtended spkr in speakerCollection)
                {
                    if (spkr.SpeakerID >= 0)
                    {
                        if(Pcc.Game == MEGame.ME3)
                        {
                            m_aSpeakerList.Add(new NameProperty("m_aSpeakerList") { Value = spkr.SpeakerName });
                        }
                        else
                        {
                            var spkrProp = new PropertyCollection();
                            spkrProp.Add(new NameProperty("sSpeakerTag") { Value = spkr.SpeakerName });
                            spkrProp.Add(new NoneProperty());
                            m_SpeakerList.Add(new StructProperty("BioDialogSpeaker", spkrProp));
                        }
                    }

                    if (spkr.FaceFX_Male == null)
                    {
                        m_aMaleFaceSets.Add(new ObjectProperty(0));
                    }
                    else
                    {
                        m_aMaleFaceSets.Add(new ObjectProperty(spkr.FaceFX_Male, "m_aMaleFaceSets"));
                    }
                    if (spkr.FaceFX_Female == null)
                    {
                        m_aFemaleFaceSets.Add(new ObjectProperty(0));
                    }
                    else
                    {
                        m_aFemaleFaceSets.Add(new ObjectProperty(spkr.FaceFX_Female, "m_aFemaleFaceSets"));
                    }
                }

                if (m_aSpeakerList.Count > 0 && Pcc.Game == MEGame.ME3)
                {
                    SelectedConv.BioConvo.AddOrReplaceProp(m_aSpeakerList);
                }
                else if(m_SpeakerList.Count > 0)
                {
                    SelectedConv.BioConvo.AddOrReplaceProp(m_SpeakerList);
                }
                if (m_aMaleFaceSets.Count > 0)
                {
                    SelectedConv.BioConvo.AddOrReplaceProp(m_aMaleFaceSets);
                }
                if (m_aFemaleFaceSets.Count > 0)
                {
                    SelectedConv.BioConvo.AddOrReplaceProp(m_aFemaleFaceSets);
                }
                PushConvoToFile(SelectedConv);
            }
            catch (Exception e)
            {
                MessageBox.Show($"Speaksave FAILED. {e}");

            }
        }

        #endregion Parsing

        #region Handling-updates
        public override void handleUpdate(List<PackageUpdate> updates)
        {
            if (Pcc == null)
            {
                return; //nothing is loaded
            }

            IEnumerable<PackageUpdate> relevantUpdates = updates.Where(x => x.change != PackageChange.Import &&
                                                                            x.change != PackageChange.ImportAdd &&
                                                                            x.change != PackageChange.Names);

            if (SelectedConv != null && CurrentLoadedExport.ClassName != "BioConversation")
            {
                //loaded convo is no longer a convo
                SelectedConv = null;
                SelectedSpeakerList.ClearEx();
                Conversations_ListBox.SelectedIndex = -1;
                graphEditor.nodeLayer.RemoveAllChildren();
                graphEditor.edgeLayer.RemoveAllChildren();
                Properties_InterpreterWPF.UnloadExport();
                LoadConversations();
                return;
            }

            if(relevantUpdates.Select(x => x.index).Where(update => Pcc.getExport(update).ClassName == "FaceFXAnimSet").Any())
            {
                FFXAnimsets.ClearEx(); //REBUILD ANIMSET LIST IF NEW ONES.
                foreach (var exp in Pcc.Exports.Where(exp => exp.ClassName == "FaceFXAnimSet"))
                {
                    FFXAnimsets.Add(exp);
                }
            }

            List<int> updatedConvos = relevantUpdates.Select(x => x.index).Where(update => Pcc.getExport(update).ClassName == "BioConversation").ToList();
            if (updatedConvos.IsEmpty())
                return;

            int cSelectedIdx = Conversations_ListBox.SelectedIndex;
            int sSelectedIdx = Speakers_ListBox.SelectedIndex;
            foreach (var uxp in updatedConvos)
            {
                var exp = Pcc.getExport(uxp);
                int index = Conversations.FindIndex(i => i.ExportUID == exp.UIndex);  //Can remove this nested loop?  How?
                Conversations.RemoveAt(index);
                Conversations.Insert(index, new ConversationExtended(exp.UIndex, exp.ObjectName, exp.GetProperties(), exp, new ObservableCollectionExtended<SpeakerExtended>(), new ObservableCollectionExtended<DialogueNodeExtended>(), new ObservableCollectionExtended<DialogueNodeExtended>()));
            }

            FirstParse();
            RefreshView();
            Conversations_ListBox.SelectedIndex = cSelectedIdx;
            Speakers_ListBox.SelectedIndex = sSelectedIdx;
        }
        #endregion Handling-updates

        #region Recents

        private readonly List<Button> RecentButtons = new List<Button>();
        public List<string> RFiles;
        private readonly string RECENTFILES_FILE = "RECENTFILES";

        private void LoadRecentList()
        {
            RecentButtons.AddRange(new[] { RecentButton1, RecentButton2, RecentButton3, RecentButton4, RecentButton5, RecentButton6, RecentButton7, RecentButton8, RecentButton9, RecentButton10 });
            Recents_MenuItem.IsEnabled = false;
            RFiles = new List<string>();
            RFiles.Clear();
            string path = DialogueEditorDataFolder + RECENTFILES_FILE;
            if (File.Exists(path))
            {
                string[] recents = File.ReadAllLines(path);
                foreach (string recent in recents)
                {
                    if (File.Exists(recent))
                    {
                        AddRecent(recent, true);
                    }
                }
            }

            RefreshRecent(false);
        }

        private void SaveRecentList()
        {
            if (!Directory.Exists(DialogueEditorDataFolder))
            {
                Directory.CreateDirectory(DialogueEditorDataFolder);
            }

            string path = DialogueEditorDataFolder + RECENTFILES_FILE;
            if (File.Exists(path))
                File.Delete(path);
            File.WriteAllLines(path, RFiles);
        }

        public void RefreshRecent(bool propogate, List<string> recents = null)
        {
            if (propogate && recents != null)
            {
                //we are posting an update to other instances of SeqEd
                foreach (var window in Application.Current.Windows)
                {
                    if (window is DialogueEditorWPF wpf && this != wpf)
                    {
                        wpf.RefreshRecent(false, RFiles);
                    }
                }
            }
            else if (recents != null)
            {
                //we are receiving an update
                RFiles = new List<string>(recents);
            }

            Recents_MenuItem.Items.Clear();
            if (RFiles.Count <= 0)
            {
                Recents_MenuItem.IsEnabled = false;
                return;
            }

            Recents_MenuItem.IsEnabled = true;

            int i = 0;
            foreach (string filepath in RFiles)
            {
                MenuItem fr = new MenuItem()
                {
                    Header = filepath.Replace("_", "__"),
                    Tag = filepath
                };
                RecentButtons[i].Visibility = Visibility.Visible;
                RecentButtons[i].Content = Path.GetFileName(filepath.Replace("_", "__"));
                RecentButtons[i].Click -= RecentFile_click;
                RecentButtons[i].Click += RecentFile_click;
                RecentButtons[i].Tag = filepath;
                RecentButtons[i].ToolTip = filepath;
                fr.Click += RecentFile_click;
                Recents_MenuItem.Items.Add(fr);
                i++;
            }

            while (i < 10)
            {
                RecentButtons[i].Visibility = Visibility.Collapsed;
                i++;
            }
        }

        private void RecentFile_click(object sender, EventArgs e)
        {
            string s = ((FrameworkElement)sender).Tag.ToString();
            if (File.Exists(s))
            {
                LoadFile(s);
            }
            else
            {
                MessageBox.Show("File does not exist: " + s);
            }
        }

        public void AddRecent(string s, bool loadingList)
        {
            RFiles = RFiles.Where(x => !x.Equals(s, StringComparison.InvariantCultureIgnoreCase)).ToList();
            if (loadingList)
            {
                RFiles.Add(s); //in order
            }
            else
            {
                RFiles.Insert(0, s); //put at front
            }

            if (RFiles.Count > 10)
            {
                RFiles.RemoveRange(10, RFiles.Count - 10);
            }

            Recents_MenuItem.IsEnabled = true;
        }

        #endregion Recents

        #region CreateGraph

        public void GenerateGraph()
        {
            CurrentObjects.ClearEx();
            graphEditor.nodeLayer.RemoveAllChildren();
            graphEditor.edgeLayer.RemoveAllChildren();
            StartPoDStarts = 0;
            StartPoDiagNodes = 0;
            StartPoDReplyNodes = 20;
            if (SelectedConv == null)
                return;


            LoadDialogueObjects();
            Layout();
            foreach (DObj o in CurrentObjects)
            {
                o.MouseDown += node_MouseDown;
                o.Click += node_Click;
            }

            graphEditor.Camera.X = 0;
            graphEditor.Camera.Y = 0;
            //if (SavedPositions.IsEmpty() && Pcc.Game != MEGame.ME1)
            //{
            //    if (CurrentFile.Contains("_LOC_INT"))
            //    {
            //        LoadDialogueObjects();
            //    }
            //    else
            //    {
            //        AutoLayout();
            //    }
            //}
        }
        public bool LoadDialogueObjects()
        {
            float x = 0;
            float y = 0;
            foreach(var entry in SelectedConv.EntryList)
            {
                CurrentObjects.Add(new DiagNode(entry, x, y, graphEditor));
            }

            foreach (var reply in SelectedConv.ReplyList)
            {
                CurrentObjects.Add(new DiagNodeReply(reply, x, y, graphEditor));
            }

            foreach (int start in GetStartingList())
            {
                CurrentObjects.Add(new DStart(start, x, y, graphEditor));
            }
            return true;
        }  

        public void Layout()
        {
            if (CurrentObjects != null && CurrentObjects.Any())
            {
                foreach (DObj obj in CurrentObjects)
                {
                    graphEditor.addNode(obj);
                }

                foreach (DObj obj in CurrentObjects)
                {
                    obj.CreateConnections(CurrentObjects);
                }

                for (int i = 0; i < CurrentObjects.Count; i++)
                {
                    DObj obj = CurrentObjects[i];
                    //SaveData savedInfo = new SaveData(-1);
                    //if (SavedPositions.Any())
                    //{
                    //    if (RefOrRefChild)
                    //        savedInfo = SavedPositions.FirstOrDefault(p => i == p.index);
                    //    else
                    //        savedInfo = SavedPositions.FirstOrDefault(p => obj.Index == p.index);
                    //}

                    //bool hasSavedPosition =
                    //    savedInfo.index == (RefOrRefChild ? i : obj.Index);
                    //if (hasSavedPosition)
                    //{
                    //    obj.Layout(savedInfo.X, savedInfo.Y);
                    //}
                    //else if (Pcc.Game == MEGame.ME1)
                    //{
                    //    obj.Layout();
                    //}
                    //else
                    //{
                    switch (obj)
                    {
                        case DStart _:
                            DStart dstart = obj as DStart;
                            float ystart = (dstart.StartNumber * 127);
                            obj.Layout(0, ystart);
                            //StartPoDStarts += obj.Height + 20;
                            break;
                        case DiagNodeReply _:
                            obj.Layout(500, StartPoDReplyNodes);
                            StartPoDReplyNodes += obj.Height + 25;
                            break;
                        case DiagNode _:
                            obj.Layout(250, StartPoDiagNodes);
                            StartPoDiagNodes += obj.Height + 25;
                            break;

                    }
                    //    }
                }

                foreach (SeqEdEdge edge in graphEditor.edgeLayer)
                {
                    ConvGraphEditor.UpdateEdge(edge);
                }
            }
        }

        private void AutoLayout()
        {
            foreach (DObj obj in CurrentObjects)
            {
                obj.SetOffset(0, 0); //remove existing positioning
            }

            const float HORIZONTAL_SPACING = 40;
            const float VERTICAL_SPACING = 20;
            var visitedNodes = new HashSet<int>();
            var eventNodes = CurrentObjects.OfType<DStart>().ToList();
            DObj firstNode = eventNodes.FirstOrDefault();
            var varNodeLookup = CurrentObjects.OfType<SVar>().ToDictionary(obj => obj.UIndex);
            var opNodeLookup = CurrentObjects.OfType<DBox>().ToDictionary(obj => obj.UIndex);
            var rootTree = new List<DObj>();
            //DStarts are natural root nodes. ALmost everything will proceed from one of these
            foreach (DStart eventNode in eventNodes)
            {
                LayoutTree(eventNode, 5 * VERTICAL_SPACING);
            }

            //Find DiagNodes with no inputs. These will not have been reached from an DStart
            var orphanRoots = CurrentObjects.OfType<DiagNode>().Where(node => node.InputEdges.IsEmpty());
            foreach (DiagNode orphan in orphanRoots)
            {
                LayoutTree(orphan, VERTICAL_SPACING);
            }

            //It's possible that there are groups of otherwise unconnected DiagNodes that form cycles.
            //Might be possible to make a better heuristic for choosing a root than sequence order, but this situation is so rare it's not worth the effort
            var cycleNodes = CurrentObjects.OfType<DiagNode>().Where(node => !visitedNodes.Contains(node.UIndex));
            foreach (DiagNode cycleNode in cycleNodes)
            {
                LayoutTree(cycleNode, VERTICAL_SPACING);
            }

            //Lonely unconnected variables. Put them in a row below everything else
            var unusedVars = CurrentObjects.OfType<SVar>().Where(obj => !visitedNodes.Contains(obj.UIndex));
            float varOffset = 0;
            float vertOffset = rootTree.BoundingRect().Bottom + VERTICAL_SPACING;
            foreach (SVar unusedVar in unusedVars)
            {
                unusedVar.OffsetBy(varOffset, vertOffset);
                varOffset += unusedVar.GlobalFullWidth + HORIZONTAL_SPACING;
            }

            if (firstNode != null) CurrentObjects.OffsetBy(0, -firstNode.OffsetY);

            foreach (SeqEdEdge edge in graphEditor.edgeLayer)
                ConvGraphEditor.UpdateEdge(edge);


            void LayoutTree(DBox DiagNode, float verticalSpacing)
            {
                if (firstNode == null) firstNode = DiagNode;
                visitedNodes.Add(DiagNode.UIndex);
                var subTree = LayoutSubTree(DiagNode);
                float width = subTree.BoundingRect().Width + HORIZONTAL_SPACING;
                //ignore nodes that are further to the right than this subtree is wide. This allows tighter spacing
                float dy = rootTree.Where(node => node.GlobalFullBounds.Left < width).BoundingRect().Bottom;
                if (dy > 0) dy += verticalSpacing;
                subTree.OffsetBy(0, dy);
                rootTree.AddRange(subTree);
            }

            List<DObj> LayoutSubTree(DBox root)
            {
                //Task.WaitAll(Task.Delay(1500));
                var tree = new List<DObj>();
                var childTrees = new List<List<DObj>>();
                var children = root.Outlinks.SelectMany(link => link.Links).Where(uIndex => !visitedNodes.Contains(uIndex));
                foreach (int uIndex in children)
                {
                    visitedNodes.Add(uIndex);
                    if (opNodeLookup.TryGetValue(uIndex, out DBox node))
                    {
                        List<DObj> subTree = LayoutSubTree(node);
                        childTrees.Add(subTree);
                    }
                }

                if (childTrees.Any())
                {
                    float dx = root.GlobalFullWidth + (HORIZONTAL_SPACING * (1 + childTrees.Count * 0.4f));
                    foreach (List<DObj> subTree in childTrees)
                    {
                        float subTreeWidth = subTree.BoundingRect().Width + HORIZONTAL_SPACING + dx;
                        //ignore nodes that are further to the right than this subtree is wide. This allows tighter spacing
                        float dy = tree.Where(node => node.GlobalFullBounds.Left < subTreeWidth).BoundingRect().Bottom;
                        if (dy > 0) dy += VERTICAL_SPACING;
                        subTree.OffsetBy(dx, dy);
                        //TODO: fix this so it doesn't screw up some sequences. eg: BioD_ProEar_310BigFall.pcc
                        /*float treeWidth = tree.BoundingRect().Width + HORIZONTAL_SPACING;
                        //tighten spacing when this subtree is wider than existing tree. 
                        dy -= subTree.Where(node => node.GlobalFullBounds.Left < treeWidth).BoundingRect().Top;
                        if (dy < 0) dy += VERTICAL_SPACING;
                        subTree.OffsetBy(0, dy);*/

                        tree.AddRange(subTree);
                    }

                    //center the root on its children
                    float centerOffset = tree.OfType<DBox>().BoundingRect().Height / 2 - root.GlobalFullHeight / 2;
                    root.OffsetBy(0, centerOffset);
                }


                tree.Add(root);
                return tree;
            }
        }

        public void RefreshView()
        {
            Properties_InterpreterWPF.LoadExport(CurrentLoadedExport);
            

            GenerateGraph();
            //saveView(false);
            //LoadSequence(SelectedSequence, false);
        }
        #endregion CreateGraph 

        #region UIHandling-boxes
        private void ConversationList_SelectedItemChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Conversations_ListBox.SelectedIndex < 0)
            {
                SelectedConv = null;
                SelectedSpeakerList.ClearEx();
                Properties_InterpreterWPF.UnloadExport();
                Convo_Panel.Visibility = Visibility.Collapsed;
            }
            else
            {
                var nconv = Conversations[Conversations_ListBox.SelectedIndex];
                SelectedConv = new ConversationExtended(nconv.ExportUID, nconv.ConvName, nconv.BioConvo, nconv.Export, nconv.IsParsed, nconv.IsFirstParsed, nconv.Speakers, nconv.EntryList, nconv.ReplyList, nconv.WwiseBank, nconv.Sequence, nconv.NonSpkrFFX);
                CurrentLoadedExport = CurrentConvoPackage.getUExport(SelectedConv.ExportUID);
                if(Pcc.Game == MEGame.ME1)
                {
                    LevelHeader.Text = "Audio/Interpdata File:";
                    LevelHeader.ToolTip = "File that contains the audio and cutscene data for the conversation";
                    Level_Textbox.ToolTip = "File that contains the audio and cutscene data for the conversation";
                    OpenLevelPackEd_Button.ToolTip = "Open Audio/Interpdata File in Package Editor";
                    OpenLevelSeqEd_Button.ToolTip = "Open Audio/Interpdata File in Sequence Editor";
                }
                else
                {
                    LevelHeader.Text = "Level:";
                    LevelHeader.ToolTip = "File with the level and sequence that uses the conversation.";
                    Level_Textbox.ToolTip = "File with the level and sequence that uses the conversation.";
                    OpenLevelPackEd_Button.ToolTip = "Open level in Package Editor";
                    OpenLevelSeqEd_Button.ToolTip = "Open level in Sequence Editor";
                }

                GenerateSpeakerList();
                RefreshView();
                Speaker_Panel.Visibility = Visibility.Collapsed;
                Convo_Panel.Visibility = Visibility.Visible;
                Node_Panel.Visibility = Visibility.Collapsed;
            }

            SelectedObjects.ClearEx();
            SelectedObjects.AddRange(CurrentObjects.Take(5));
            
            if (SelectedObjects.Any())
            {
                if (panToSelection)
                {
                    if (SelectedObjects.Count == 1)
                    {
                        graphEditor.Camera.AnimateViewToCenterBounds(SelectedObjects[0].GlobalFullBounds, false, 100);
                    }
                    else
                    {
                        RectangleF boundingBox = SelectedObjects.Select(obj => obj.GlobalFullBounds).BoundingRect();
                        graphEditor.Camera.AnimateViewToCenterBounds(boundingBox, true, 200);
                    }
                }
            }

            panToSelection = true;
            graphEditor.Refresh();
        }
        private void Convo_ListBox_MouseEnter(object sender, MouseEventArgs e)
        {
            Speaker_Panel.Visibility = Visibility.Collapsed;
            Convo_Panel.Visibility = Visibility.Visible;
            Node_Panel.Visibility = Visibility.Collapsed;
        }
        private void Convo_NSFFX_DropDownClosed(object sender, EventArgs e)
        {
            
            if(SelectedConv.NonSpkrFFX != FFXAnimsets[ComboBox_Conv_NSFFX.SelectedIndex]);
            {
                SelectedConv.NonSpkrFFX = FFXAnimsets[ComboBox_Conv_NSFFX.SelectedIndex];
                SelectedConv.BioConvo.AddOrReplaceProp(new ObjectProperty(SelectedConv.NonSpkrFFX, new NameReference("m_pNonSpeakerFaceFXSet")));
                PushConvoToFile(SelectedConv);
            }
        }

        private void Speakers_ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if( !SelectedSpeakerList.IsEmpty())
            {
                if (Speakers_ListBox.SelectedIndex >= 0)
                {
                    var newspeaker = SelectedSpeakerList.First(spkr => spkr.SpeakerID == Speakers_ListBox.SelectedIndex - 2);

                    SelectedSpeaker.SpeakerID = newspeaker.SpeakerID;
                    SelectedSpeaker.SpeakerName = newspeaker.SpeakerName;
                    SelectedSpeaker.FaceFX_Male = newspeaker.FaceFX_Male;
                    SelectedSpeaker.FaceFX_Female = newspeaker.FaceFX_Female;
                    SelectedSpeaker.StrRefID = newspeaker.StrRefID;
                    SelectedSpeaker.FriendlyName = newspeaker.FriendlyName;

                    if(SelectedSpeaker.SpeakerID < 0)
                    {
                        TextBox_Speaker_Name.IsEnabled = false;
                    }
                    else
                    {
                        TextBox_Speaker_Name.IsEnabled = true;
                    }
                    Speaker_Panel.Visibility = Visibility.Visible;
                    Convo_Panel.Visibility = Visibility.Collapsed;
                    Node_Panel.Visibility = Visibility.Collapsed;
                    //HIDE OTHER PANELS BY SWITCHING OFF SELECTION
                }
                else
                {
                   
                    SelectedSpeaker.SpeakerID = -3;
                    SelectedSpeaker.SpeakerName = "None";
                }
            }
        }
        private void SpeakerMoveAction(object obj)
        {
            SpkrUpButton.IsEnabled = false;
            SpkrDownButton.IsEnabled = false;
            string direction = obj as string;
            int n = 1; //Movement default is down the list (higher n)
            if (direction == "Up")
            {
                n = -1;
            }


            int selectedIndex = Speakers_ListBox.SelectedIndex;
            Speakers_ListBox.SelectedIndex = -1;

            var OldSpkrList = new ObservableCollectionExtended<SpeakerExtended>(SelectedSpeakerList);
            SelectedSpeakerList.ClearEx();

            var itemToMove = OldSpkrList[selectedIndex];
            itemToMove.SpeakerID = selectedIndex + n - 2;
            OldSpkrList[selectedIndex + n].SpeakerID = selectedIndex - 2;
            OldSpkrList.RemoveAt(selectedIndex);
            OldSpkrList.Insert(selectedIndex + n, itemToMove);

            foreach (var spkr in OldSpkrList)
            {
                SelectedSpeakerList.Add(spkr);
            }
            SelectedConv.Speakers = new ObservableCollectionExtended<SpeakerExtended>(SelectedSpeakerList);
            Speakers_ListBox.SelectedIndex = selectedIndex + n;
            SpkrUpButton.IsEnabled = true;
            SpkrDownButton.IsEnabled = true;
            SaveSpeakersToProperties(SelectedSpeakerList);
        }
        private void ComboBox_Speaker_FFX_DropDownClosed(object sender, EventArgs e)
        {
            var ffxMaleNew = SelectedSpeaker.FaceFX_Male;
            var ffxMaleOld = SelectedSpeakerList[SelectedSpeaker.SpeakerID + 2].FaceFX_Male;
            var ffxFemaleNew = SelectedSpeaker.FaceFX_Female;
            var ffxFemaleOld = SelectedSpeakerList[SelectedSpeaker.SpeakerID + 2].FaceFX_Female;
            if (ffxMaleNew == ffxMaleOld && ffxFemaleNew == ffxFemaleOld)
                return;

            System.Media.SystemSounds.Question.Play();
            var dlg = MessageBox.Show("Are you sure you want to change this speaker's facial animation set? (Not recommended)", "WARNING", MessageBoxButton.OKCancel);
            if (dlg == MessageBoxResult.Cancel)
            {
                SelectedSpeaker.FaceFX_Male = ffxMaleOld;
                SelectedSpeaker.FaceFX_Female = ffxFemaleOld;
                return;
            }

                

            SelectedSpeakerList[SelectedSpeaker.SpeakerID + 2].FaceFX_Male = ffxMaleNew;
            SelectedSpeakerList[SelectedSpeaker.SpeakerID + 2].FaceFX_Female = ffxFemaleNew;

            SaveSpeakersToProperties(SelectedSpeakerList);

        }
        private void EnterName_Speaker_KeyUp(object sender, KeyEventArgs e)
        {
            if(e.Key == Key.Enter)
            {
               var dlg = MessageBox.Show("Do you want to change this actors name?","Confirm",MessageBoxButton.YesNo);
                if (dlg == MessageBoxResult.No)
                {
                    return;
                }
                else
                {
                    Keyboard.ClearFocus();
                    SelectedSpeakerList[Speakers_ListBox.SelectedIndex].SpeakerName = SelectedSpeaker.SpeakerName;
                    SaveSpeakersToProperties(SelectedSpeakerList);
                }
            }
        }
        private void EnterName_Speaker_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            TextBox_Speaker_Name.BorderThickness = new Thickness(2, 2, 2, 2);
            TextBox_Speaker_Name.Background = System.Windows.Media.Brushes.GhostWhite;
        }
        private void EnterName_Speaker_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            TextBox_Speaker_Name.BorderThickness = new Thickness(0, 0, 0, 0);
            TextBox_Speaker_Name.Background = System.Windows.Media.Brushes.White;
        }
        private void SpeakerAdd()
        {
            int maxID = SelectedSpeakerList.Max(x => x.SpeakerID);
            var ndlg = new PromptDialog("Enter the new actors tag", "Add a speaker", "Actor_Tag");
            ndlg.ShowDialog();
            if (ndlg.ResponseText == null || ndlg.ResponseText == "Actor_Tag")
                return;
            Pcc.FindNameOrAdd(ndlg.ResponseText);
            SelectedSpeakerList.Add(new SpeakerExtended(maxID + 1, ndlg.ResponseText, null, null, 0, "No Data"));
            SaveSpeakersToProperties(SelectedSpeakerList);
        }
        private void SpeakerDelete()
        {
            var deleteTarget = Speakers_ListBox.SelectedIndex;
            if (deleteTarget < 2)
            {
                MessageBox.Show("Owner and Player speakers cannot be deleted.", "Dialogue Editor");
                return;
            }

            string delName = SelectedSpeakerList[deleteTarget].SpeakerName;

            var dlg = MessageBox.Show($"Are you sure you want to delete {delName}? ", "Warning: Speaker Deletion", MessageBoxButton.OKCancel);
            
            if(dlg == MessageBoxResult.Cancel)
                return;

            MessageBox.Show("TODO: CHECK FOR ANY CONVERSATION NODES THAT HAVE THIS SPEAKER. AND BLOCK.", "Dialogue Editor");    //TODO: CHECK FOR ANY CONVERSATION NODES THAT HAVE THIS SPEAKER. AND BLOCK.

            SelectedConv.Speakers.RemoveAt(deleteTarget);
            SelectedSpeakerList.RemoveAt(deleteTarget);
            SaveSpeakersToProperties(SelectedSpeakerList);
        }
        private void SpeakerGoToName()
        {
            TextBox_Speaker_Name.Focus();
            TextBox_Speaker_Name.CaretIndex = TextBox_Speaker_Name.Text.Length;
        }

        #endregion

        #region UIHandling-graph

        private void backMouseDown_Handler(object sender, PInputEventArgs e)
        {
            if (!(e.PickedNode is PCamera) || SelectedConv == null) return;

            if (e.Button == System.Windows.Forms.MouseButtons.Right)
            {
                if (FindResource("backContextMenu") is ContextMenu contextMenu)
                {
                    contextMenu.IsOpen = true;
                }
            }
            else if (e.Shift)
            {
                //graphEditor.StartBoxSelection(e);
                //e.Handled = true;
            }
            else
            {
                //Conversations_ListBox.SelectedItems.Clear();
            }
        }

        private void back_MouseUp(object sender, PInputEventArgs e)
        {
            //var nodesToSelect = graphEditor.OfType<DObj>();
            //foreach (DObj DObj in nodesToSelect)
            //{
            //    panToSelection = false;
            //    .SelectedItems.Add(DObj);
            //}
        }

        private void graphEditor_Click(object sender, EventArgs e)
        {
            graphEditor.Focus();
        }

        private void SequenceEditor_DragEnter(object sender, System.Windows.Forms.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.Forms.DataFormats.FileDrop))
                e.Effect = System.Windows.Forms.DragDropEffects.All;
            else
                e.Effect = System.Windows.Forms.DragDropEffects.None;
        }

        private void SequenceEditor_DragDrop(object sender, System.Windows.Forms.DragEventArgs e)
        {
            if (e.Data.GetData(System.Windows.Forms.DataFormats.FileDrop) is string[] DroppedFiles)
            {
                if (DroppedFiles.Any())
                {
                    LoadFile(DroppedFiles[0]);
                }
            }
        }



        private readonly List<SaveData> extraSaveData = new List<SaveData>();
        private bool panToSelection = true;
        private string FileQueuedForLoad;
        private IExportEntry ExportQueuedForFocusing;

        private void saveView(bool toFile = true)
        {
            if (CurrentObjects.Count == 0)
                return;
            SavedPositions = new List<SaveData>();
            for (int i = 0; i < CurrentObjects.Count; i++)
            {
                DObj obj = CurrentObjects[i];
                if (obj.Pickable)
                {
                    SavedPositions.Add(new SaveData
                    {
                        absoluteIndex = RefOrRefChild,
                        index = RefOrRefChild ? i : obj.Index,
                        X = obj.X + obj.Offset.X,
                        Y = obj.Y + obj.Offset.Y
                    });
                }
            }

            SavedPositions.AddRange(extraSaveData);
            extraSaveData.Clear();

            if (toFile)
            {
                string outputFile = JsonConvert.SerializeObject(SavedPositions);
                if (!Directory.Exists(Path.GetDirectoryName(JSONpath)))
                    Directory.CreateDirectory(Path.GetDirectoryName(JSONpath));
                File.WriteAllText(JSONpath, outputFile);
                SavedPositions.Clear();
            }
        }

        public void OpenNodeContextMenu(DObj obj)
        {
            if (FindResource("nodeContextMenu") is ContextMenu contextMenu)
            {
                if (obj is DBox DBox && (DBox.Outlinks.Any())
                 && contextMenu.GetChild("breakLinksMenuItem") is MenuItem breakLinksMenuItem)
                {
                    bool hasLinks = false;
                    if (breakLinksMenuItem.GetChild("outputLinksMenuItem") is MenuItem outputLinksMenuItem)
                    {
                        outputLinksMenuItem.Visibility = Visibility.Collapsed;
                        outputLinksMenuItem.Items.Clear();
                        for (int i = 0; i < DBox.Outlinks.Count; i++)
                        {
                            for (int j = 0; j < DBox.Outlinks[i].Links.Count; j++)
                            {
                                outputLinksMenuItem.Visibility = Visibility.Visible;
                                hasLinks = true;
                                var temp = new MenuItem
                                {
                                    Header = $"Break link from {DBox.Outlinks[i].Desc} to {DBox.Outlinks[i].Links[j]}"
                                };
                                int linkConnection = i;
                                int linkIndex = j;
                                temp.Click += (o, args) => { DBox.RemoveOutlink(linkConnection, linkIndex); };
                                outputLinksMenuItem.Items.Add(temp);
                            }
                        }
                    }

                    if (breakLinksMenuItem.GetChild("breakAllLinksMenuItem") is MenuItem breakAllLinksMenuItem)
                    {
                        if (hasLinks)
                        {
                            breakLinksMenuItem.Visibility = Visibility.Visible;
                            breakAllLinksMenuItem.Visibility = Visibility.Visible;
                            breakAllLinksMenuItem.Tag = obj.Export;
                        }
                        else
                        {
                            breakLinksMenuItem.Visibility = Visibility.Collapsed;
                            breakAllLinksMenuItem.Visibility = Visibility.Collapsed;
                        }
                    }
                }

                if (contextMenu.GetChild("interpViewerMenuItem") is MenuItem interpViewerMenuItem)
                {
                    //string className = obj.Export.ClassName;
                }

                if (contextMenu.GetChild("plotEditorMenuItem") is MenuItem plotEditorMenuItem)
                {

                    if (Pcc.Game == MEGame.ME3 && obj is DiagNode DiagNode &&
                        DiagNode.Export.ClassName == "BioSeqAct_PMExecuteTransition" &&
                        DiagNode.Export.GetProperty<IntProperty>("m_nIndex") != null)
                    {
                        plotEditorMenuItem.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        plotEditorMenuItem.Visibility = Visibility.Collapsed;
                    }
                }

                contextMenu.IsOpen = true;
                graphEditor.DisableDragging();
            }
        }

        private void removeAllLinks(object sender, RoutedEventArgs args)
        {
            IExportEntry export = (IExportEntry)((MenuItem)sender).Tag;
            removeAllLinks(export);
        }

        private static void removeAllLinks(IExportEntry export)
        {
            var props = export.GetProperties();
            var outLinksProp = props.GetProp<ArrayProperty<StructProperty>>("OutputLinks");
            if (outLinksProp != null)
            {
                foreach (var prop in outLinksProp)
                {
                    prop.GetProp<ArrayProperty<StructProperty>>("Links").Clear();
                }
            }

            var varLinksProp = props.GetProp<ArrayProperty<StructProperty>>("VariableLinks");
            if (varLinksProp != null)
            {
                foreach (var prop in varLinksProp)
                {
                    prop.GetProp<ArrayProperty<ObjectProperty>>("LinkedVariables").Clear();
                }
            }

            var eventLinksProp = props.GetProp<ArrayProperty<StructProperty>>("EventLinks");
            if (eventLinksProp != null)
            {
                foreach (var prop in eventLinksProp)
                {
                    prop.GetProp<ArrayProperty<ObjectProperty>>("LinkedEvents").Clear();
                }
            }

            export.WriteProperties(props);
        }

        private void RemoveFromDialogue_Click(object sender, RoutedEventArgs e)
        {
            if (Conversations_ListBox.SelectedItem is DObj DObj)
            {
                //remove incoming connections
                switch (DObj)
                {
                    case DiagNode DiagNode:
                        foreach (DBox.InputLink inLink in DiagNode.InLinks)
                        {
                            foreach (ActionEdge edge in inLink.Edges)
                            {
                                edge.originator.RemoveOutlink(edge);
                            }
                        }
                        break;
                }

                //remove outgoing links
                removeAllLinks(DObj.Export);

                //remove from sequence
                //var seqObjs = SelectedSequence.GetProperty<ArrayProperty<ObjectProperty>>("SequenceObjects");
                //var arrayObj = seqObjs?.FirstOrDefault(x => x.Value == DObj.UIndex);
                //if (arrayObj != null)
                //{
                //    seqObjs.Remove(arrayObj);
                //    SelectedSequence.WriteProperty(seqObjs);
                //}

                //set ParentSequence to null
                var parentSeqProp = DObj.Export.GetProperty<ObjectProperty>("ParentSequence");
                parentSeqProp.Value = 0;
                DObj.Export.WriteProperty(parentSeqProp);

            }
        }

        protected void node_MouseDown(object sender, PInputEventArgs e)
        {
            if (sender is DObj obj)
            {
                obj.posAtDragStart = obj.GlobalFullBounds;
                if (e.Button == System.Windows.Forms.MouseButtons.Right)
                {
                    panToSelection = false;
                    if (SelectedObjects.Count > 1)
                    {
                        panToSelection = false;
                    }
                    OpenNodeContextMenu(obj);
                }
                else if (e.Shift || e.Control)
                {
                    panToSelection = false;
                    if (obj.IsSelected)
                    {
                        Conversations_ListBox.SelectedItems.Remove(obj);
                    }
                    else
                    {
                    }
                }
                else if (!obj.IsSelected)
                {
                    panToSelection = false;
                }
            }
        }

        private void node_Click(object sender, PInputEventArgs e)
        {
            if (sender is DiagNode obj)
            {
                if (e.Button != System.Windows.Forms.MouseButtons.Left && obj.GlobalFullBounds == obj.posAtDragStart)
                {
                    if (!e.Shift && !e.Control)
                    {
                        if (SelectedObjects.Count == 1 && obj.IsSelected) return;
                        panToSelection = false;
                        if (SelectedObjects.Count > 1)
                        {
                            panToSelection = false;
                        }
                    }
                }
                else
                {
                    foreach(var oldselection in SelectedObjects)
                    {
                        oldselection.IsSelected = false;
                    }
                    obj.IsSelected = true;
                    SelectedObjects.Add(obj);
                    var newnode = obj.Node;
                    SelectedDialogueNode = newnode;
                    if (newnode.IsReply == true)
                    {
                        //SelectedDialogueNode.IsReply = true;
                         //this doesn't work
                        //ActiveDialogueNode = new DialogueNodeExtended(newnode.Node, newnode.IsReply, newnode.NodeCount, newnode.SpeakerIndex, newnode.LineStrRef, newnode.Line, newnode.bFireConditional, newnode.ConditionalOrBool,
                        //    newnode.StateEvent, newnode.SpeakerTag, newnode.Interpdata, newnode.WwiseStream_Male, newnode.WwiseStream_Female, newnode.FaceFX_Male, newnode.FaceFX_Female); //doesn't work
                        //ActiveDialogueNode.Line = newnode.Line; //works

                    }


                    Speaker_Panel.Visibility = Visibility.Collapsed;
                    Convo_Panel.Visibility = Visibility.Collapsed;
                    Node_Panel.Visibility = Visibility.Visible;
                }
            }
        }

        private void CloneObject_Clicked(object sender, RoutedEventArgs e)
        {
            //if (Conversations_ListBox.SelectedItem is DObj obj)
            //{
            //    cloneObject(obj.Export, SelectedSequence);
            //}
        }

        static void cloneObject(IExportEntry old, IExportEntry sequence, bool topLevel = true)
        {
            IMEPackage pcc = sequence.FileRef;
            IExportEntry exp = old.Clone();
            //needs to have the same index to work properly
            if (exp.ClassName == "SeqVar_External")
            {
                exp.indexValue = old.indexValue;
            }

            pcc.addExport(exp);
            addObjectToSequence(exp, topLevel, sequence);
            cloneSequence(exp, sequence);
        }

        static void addObjectToSequence(IExportEntry newObject, bool removeLinks, IExportEntry sequenceExport)
        {
            var seqObjs = sequenceExport.GetProperty<ArrayProperty<ObjectProperty>>("SequenceObjects");
            if (seqObjs != null)
            {
                seqObjs.Add(new ObjectProperty(newObject));
                sequenceExport.WriteProperty(seqObjs);

                PropertyCollection newObjectProps = newObject.GetProperties();
                newObjectProps.AddOrReplaceProp(new ObjectProperty(sequenceExport, "ParentSequence"));
                if (removeLinks)
                {
                    var outLinksProp = newObjectProps.GetProp<ArrayProperty<StructProperty>>("OutputLinks");
                    if (outLinksProp != null)
                    {
                        foreach (var prop in outLinksProp)
                        {
                            prop.GetProp<ArrayProperty<StructProperty>>("Links").Clear();
                        }
                    }

                    var varLinksProp = newObjectProps.GetProp<ArrayProperty<StructProperty>>("VariableLinks");
                    if (varLinksProp != null)
                    {
                        foreach (var prop in varLinksProp)
                        {
                            prop.GetProp<ArrayProperty<ObjectProperty>>("LinkedVariables").Clear();
                        }
                    }
                }

                newObject.WriteProperties(newObjectProps);
                newObject.idxLink = sequenceExport.UIndex;
            }
        }

        static void cloneSequence(IExportEntry exp, IExportEntry parentSequence)
        {
            IMEPackage pcc = exp.FileRef;
            if (exp.ClassName == "Sequence")
            {
                var seqObjs = exp.GetProperty<ArrayProperty<ObjectProperty>>("SequenceObjects");
                if (seqObjs == null || seqObjs.Count == 0)
                {
                    return;
                }

                //store original list of sequence objects;
                List<int> oldObjects = seqObjs.Select(x => x.Value).ToList();

                //clear original sequence objects
                seqObjs.Clear();
                exp.WriteProperty(seqObjs);

                //clone all children
                foreach (var obj in oldObjects)
                {
                    cloneObject(pcc.getUExport(obj), exp, false);
                }

                //re-point children's links to new objects
                seqObjs = exp.GetProperty<ArrayProperty<ObjectProperty>>("SequenceObjects");
                foreach (var seqObj in seqObjs)
                {
                    IExportEntry obj = pcc.getUExport(seqObj.Value);
                    var props = obj.GetProperties();
                    var outLinksProp = props.GetProp<ArrayProperty<StructProperty>>("OutputLinks");
                    if (outLinksProp != null)
                    {
                        foreach (var outLinkStruct in outLinksProp)
                        {
                            var links = outLinkStruct.GetProp<ArrayProperty<StructProperty>>("Links");
                            foreach (var link in links)
                            {
                                var linkedOp = link.GetProp<ObjectProperty>("LinkedOp");
                                linkedOp.Value = seqObjs[oldObjects.IndexOf(linkedOp.Value)].Value;
                            }
                        }
                    }

                    var varLinksProp = props.GetProp<ArrayProperty<StructProperty>>("VariableLinks");
                    if (varLinksProp != null)
                    {
                        foreach (var varLinkStruct in varLinksProp)
                        {
                            var links = varLinkStruct.GetProp<ArrayProperty<ObjectProperty>>("LinkedVariables");
                            foreach (var link in links)
                            {
                                link.Value = seqObjs[oldObjects.IndexOf(link.Value)].Value;
                            }
                        }
                    }

                    var eventLinksProp = props.GetProp<ArrayProperty<StructProperty>>("EventLinks");
                    if (eventLinksProp != null)
                    {
                        foreach (var eventLinkStruct in eventLinksProp)
                        {
                            var links = eventLinkStruct.GetProp<ArrayProperty<ObjectProperty>>("LinkedEvents");
                            foreach (var link in links)
                            {
                                link.Value = seqObjs[oldObjects.IndexOf(link.Value)].Value;
                            }
                        }
                    }

                    obj.WriteProperties(props);
                }

                //re-point sequence links to new objects
                int oldObj;
                int newObj;
                var propCollection = exp.GetProperties();
                var inputLinksProp = propCollection.GetProp<ArrayProperty<StructProperty>>("InputLinks");
                if (inputLinksProp != null)
                {
                    foreach (var inLinkStruct in inputLinksProp)
                    {
                        var linkedOp = inLinkStruct.GetProp<ObjectProperty>("LinkedOp");
                        oldObj = linkedOp.Value;
                        if (oldObj != 0)
                        {
                            newObj = seqObjs[oldObjects.IndexOf(oldObj)].Value;
                            linkedOp.Value = newObj;

                            NameProperty linkAction = inLinkStruct.GetProp<NameProperty>("LinkAction");
                            linkAction.Value = new NameReference(linkAction.Value.Name, pcc.getUExport(newObj).indexValue);
                        }
                    }
                }

                var outputLinksProp = propCollection.GetProp<ArrayProperty<StructProperty>>("OutputLinks");
                if (outputLinksProp != null)
                {
                    foreach (var outLinkStruct in outputLinksProp)
                    {
                        var linkedOp = outLinkStruct.GetProp<ObjectProperty>("LinkedOp");
                        oldObj = linkedOp.Value;
                        if (oldObj != 0)
                        {
                            newObj = seqObjs[oldObjects.IndexOf(oldObj)].Value;
                            linkedOp.Value = newObj;

                            NameProperty linkAction = outLinkStruct.GetProp<NameProperty>("LinkAction");
                            linkAction.Value = new NameReference(linkAction.Value.Name, pcc.getUExport(newObj).indexValue);
                        }
                    }
                }

                exp.WriteProperties(propCollection);
            }
            else if (exp.ClassName == "SequenceReference")
            {
                //set OSequenceReference to new sequence
                var oSeqRefProp = exp.GetProperty<ObjectProperty>("oSequenceReference");
                if (oSeqRefProp == null || oSeqRefProp.Value == 0)
                {
                    return;
                }

                int oldSeqIndex = oSeqRefProp.Value;
                oSeqRefProp.Value = exp.UIndex + 1;
                exp.WriteProperty(oSeqRefProp);

                //clone sequence
                cloneObject(pcc.getUExport(oldSeqIndex), parentSequence, false);

                //remove cloned sequence from SeqRef's parent's sequenceobjects
                var seqObjs = parentSequence.GetProperty<ArrayProperty<ObjectProperty>>("SequenceObjects");
                seqObjs.RemoveAt(seqObjs.Count - 1);
                parentSequence.WriteProperty(seqObjs);

                //set SequenceReference's linked name indices
                var inputIndices = new List<int>();
                var outputIndices = new List<int>();

                IExportEntry newSequence = pcc.getUExport(exp.UIndex + 1);
                var props = newSequence.GetProperties();
                var inLinksProp = props.GetProp<ArrayProperty<StructProperty>>("InputLinks");
                if (inLinksProp != null)
                {
                    foreach (var inLink in inLinksProp)
                    {
                        inputIndices.Add(inLink.GetProp<NameProperty>("LinkAction").Value.Number);
                    }
                }

                var outLinksProp = props.GetProp<ArrayProperty<StructProperty>>("OutputLinks");
                if (outLinksProp != null)
                {
                    foreach (var outLinks in outLinksProp)
                    {
                        outputIndices.Add(outLinks.GetProp<NameProperty>("LinkAction").Value.Number);
                    }
                }

                props = exp.GetProperties();
                inLinksProp = props.GetProp<ArrayProperty<StructProperty>>("InputLinks");
                if (inLinksProp != null)
                {
                    for (int i = 0; i < inLinksProp.Count; i++)
                    {
                        NameProperty linkAction = inLinksProp[i].GetProp<NameProperty>("LinkAction");
                        linkAction.Value = new NameReference(linkAction.Value.Name, inputIndices[i]);
                    }
                }

                outLinksProp = props.GetProp<ArrayProperty<StructProperty>>("OutputLinks");
                if (outLinksProp != null)
                {
                    for (int i = 0; i < outLinksProp.Count; i++)
                    {
                        NameProperty linkAction = outLinksProp[i].GetProp<NameProperty>("LinkAction");
                        linkAction.Value = new NameReference(linkAction.Value.Name, outputIndices[i]);
                    }
                }

                exp.WriteProperties(props);

                //set new Sequence's link and ParentSequence prop to SeqRef
                newSequence.WriteProperty(new ObjectProperty(exp.UIndex, "ParentSequence"));
                newSequence.idxLink = exp.UIndex;

                //set DefaultViewZoom to magic number to flag that this is a cloned Sequence Reference and global saves cannot be used with it
                //ugly, but it should work
                newSequence.WriteProperty(new FloatProperty(CLONED_SEQREF_MAGIC, "DefaultViewZoom"));
            }
        }

        private void ContextMenu_Closed(object sender, RoutedEventArgs e)
        {
            graphEditor.AllowDragging();
            Focus(); //this will make window bindings work, as context menu is not part of the visual tree, and focus will be on there if the user clicked it.
        }

        private void SaveImage()
        {
            if (CurrentObjects.Count == 0)
                return;
            string objectName = System.Text.RegularExpressions.Regex.Replace(CurrentLoadedExport.ObjectName, @"[<>:""/\\|?*]", "");
            SaveFileDialog d = new SaveFileDialog
            {
                Filter = "PNG Files (*.png)|*.png",
                FileName = $"{CurrentFile}.{objectName}"
            };
            if (d.ShowDialog() == true)
            {
                PNode r = graphEditor.Root;
                RectangleF rr = r.GlobalFullBounds;
                PNode p = PPath.CreateRectangle(rr.X, rr.Y, rr.Width, rr.Height);
                p.Brush = Brushes.White;
                graphEditor.addBack(p);
                graphEditor.Camera.Visible = false;
                System.Drawing.Image image = graphEditor.Root.ToImage();
                graphEditor.Camera.Visible = true;
                image.Save(d.FileName, ImageFormat.Png);
                graphEditor.backLayer.RemoveAllChildren();
                MessageBox.Show("Done.");
            }
        }

        private void addObject(IExportEntry exportToAdd, bool removeLinks = true)
        {
            //extraSaveData.Add(new SaveData
            //{
            //    index = exportToAdd.Index,
            //    X = graphEditor.Camera.Bounds.X + graphEditor.Camera.Bounds.Width / 2,
            //    Y = graphEditor.Camera.Bounds.Y + graphEditor.Camera.Bounds.Height / 2
            //});
            //addObjectToSequence(exportToAdd, removeLinks, SelectedSequence);
        }

        private void AddObject_Clicked(object sender, RoutedEventArgs e)
        {
            if (EntrySelector.GetEntry(this, Pcc, EntrySelector.SupportedTypes.Exports) is IExportEntry exportToAdd)
            {
                if (!exportToAdd.inheritsFrom("SequenceObject"))
                {
                    MessageBox.Show($"#{exportToAdd.UIndex}: {exportToAdd.ObjectName} is not a sequence object.");
                    return;
                }

                if (CurrentObjects.Any(obj => obj.Export == exportToAdd))
                {
                    MessageBox.Show($"#{exportToAdd.UIndex}: {exportToAdd.ObjectName} is already in the sequence.");
                    return;
                }

                addObject(exportToAdd);
            }
        }

        private void showOutputNumbers_Click(object sender, EventArgs e)
        {
            DObj.OutputNumbers = ShowOutputNumbers_MenuItem.IsChecked;
            if (CurrentObjects.Any())
            {
                RefreshView();
            }

        }

        #endregion UIHandling-graph

        #region UIHandling-menus
        private void OpenInInterpViewer_Clicked(object sender, RoutedEventArgs e)
        {
            if (Pcc.Game != MEGame.ME3)
            {
                MessageBox.Show("InterpViewer does not support ME1 or ME2 yet.", "Sorry!", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (Conversations_ListBox.SelectedItem is DObj obj)
            {
                var p = new InterpEditor();
                p.Show();
                p.LoadPCC(Pcc.FileName);
                IExportEntry exportEntry = obj.Export;
                if (exportEntry.ObjectName == "InterpData")
                {
                    p.toolStripComboBox1.SelectedIndex = p.objects.IndexOf(exportEntry.Index);
                    p.loadInterpData(exportEntry.Index);
                }
                else
                {
                    ////int i = ((DiagNode)obj).Varlinks[0].Links[0] - 1; //0-based index because Interp Viewer is old
                    //p.toolStripComboBox1.SelectedIndex = p.objects.IndexOf(i);
                    //p.loadInterpData(i);
                }
            }
        }


        private void OpenInAction(object obj)
        {
            
            string tool = obj as string;
            switch(tool)
            {
                case "PackEdLvl":
                    OpenInToolkit("PackageEditor", 0, Level);
                    break;
                case "PackEdNode":
                    OpenInToolkit("PackageEditor", SelectedConv.ExportUID);
                    break;
                case "SeqEdLvl":
                    OpenInToolkit("SequenceEditor", 0, Level);
                    break;
                case "SeqEdNode":
                    if (SelectedConv.Sequence.UIndex < 0)
                    {
                        OpenInToolkit("SequenceEditor", 0, Level);
                    }
                    else
                    {
                        OpenInToolkit("SequenceEditor", SelectedConv.Sequence.UIndex);
                    }
                    break;
                case "FaceFXNS":
                    OpenInToolkit("FaceFXEditor", SelectedConv.NonSpkrFFX.UIndex);
                    break;
                case "FaceFXSpkrM":
                    if(Pcc.isUImport(SelectedSpeaker.FaceFX_Male.UIndex))
                    {
                        OpenInToolkit("FaceFXEditor", 0, Level); //CAN SEND TO THE CORRECT EXPORT IN THE NEW FILE LOAD?
                    }
                    else
                    {
                        OpenInToolkit("FaceFXEditor", SelectedSpeaker.FaceFX_Male.UIndex);
                    }
                    break;
                case "FaceFXSpkrF":
                    if (Pcc.isUImport(SelectedSpeaker.FaceFX_Female.UIndex))
                    {
                        OpenInToolkit("FaceFXEditor", 0, Level);
                    }
                    else
                    {
                        OpenInToolkit("FaceFXEditor", SelectedSpeaker.FaceFX_Female.UIndex);
                    }
                    break;
                default:
                    OpenInToolkit(tool);
                    break;
            }
        }

        private void OpenInToolkit(string tool, int export = 0, string filename  = null)
        {
            string filePath = null;
            if(filename != null)  //If file is a new loaded file need to find path.
            {
                filePath = Path.Combine(Path.GetDirectoryName(Pcc.FileName), filename);

                if (!File.Exists(filePath))
                {
                    string rootPath = null;
                    switch (Pcc.Game)
                    {
                        case MEGame.ME1:
                            rootPath = ME1Directory.gamePath;
                            break;
                        case MEGame.ME2:
                            rootPath = ME2Directory.gamePath;
                            break;
                        case MEGame.ME3:
                            rootPath = ME3Directory.gamePath;
                            break;
                    }
                    filePath = Directory.GetFiles(rootPath, Level, SearchOption.AllDirectories).FirstOrDefault();
                    if (filePath == null)
                    {
                        MessageBox.Show($"File {filename} not found.");
                        return;
                    }
                    var dlg = MessageBox.Show($"Opening level at {filePath}", "Dialogue Editor", MessageBoxButton.OKCancel);
                    if (dlg == MessageBoxResult.Cancel)
                    {
                        return;
                    }
                }
            }

            if (filePath == null)
            {
                filePath = Pcc.FileName;
            }

            switch (tool)
            {

                case "FaceFXEditor":
                    if (Pcc.isUExport(export))
                    {
                        new FaceFX.FaceFXEditor(Pcc.getUExport(export)).Show();
                    }
                    else
                    {
                        var facefxEditor = new FaceFX.FaceFXEditor();
                        facefxEditor.LoadFile(filePath);
                        facefxEditor.Show();
                    }
                    break;
                case "PackageEditor":
                    var packEditor = new PackageEditorWPF();
                    packEditor.Show();
                    if (Pcc.isUExport(export))
                    {
                        packEditor.LoadFile(Pcc.FileName, export);
                    }
                    else
                    {
                        packEditor.LoadFile(filePath);
                    }
                    break;
                case "SoundplorerWPF":
                    var soundplorerWPF = new Soundplorer.SoundplorerWPF();
                    soundplorerWPF.LoadFile(Pcc.FileName);
                    soundplorerWPF.Show();
                    break;
                case "SequenceEditor":
                    if (Pcc.isUExport(export))
                    {
                        new SequenceEditorWPF(Pcc.getUExport(export)).Show();
                    }
                    else
                    {
                        var seqEditor = new SequenceEditorWPF();
                        seqEditor.LoadFile(filePath);
                        seqEditor.Show();
                    }
                    break;
            }
        }

        private void LoadTLKManager()
        {
            if (!Application.Current.Windows.OfType<TlkManagerNS.TLKManagerWPF>().Any())
            {
                TlkManagerNS.TLKManagerWPF m = new TlkManagerNS.TLKManagerWPF();
                m.Show();
            }
            else
            {
                Application.Current.Windows.OfType<TlkManagerNS.TLKManagerWPF>().First().Focus();
            }
        }









        #endregion

    }
}