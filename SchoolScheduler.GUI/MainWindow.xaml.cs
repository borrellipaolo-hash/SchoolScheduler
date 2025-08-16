using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Win32;
using SchoolScheduler.Common.Models;
using SchoolScheduler.GUI.Services;
using OrTools = Google.OrTools.Sat;

namespace SchoolScheduler.GUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private EngineInterop? _engine;
        private ExcelImportService _importService = new ExcelImportService();
        private List<Activity> _allActivities = new List<Activity>();
        private List<Teacher> _teachers = new List<Teacher>();
        private List<SchoolClass> _classes = new List<SchoolClass>();
        private ScheduleConfiguration _configuration = new ScheduleConfiguration();
        private List<Constraint> _constraints = new List<Constraint>();
        private GeneratedSchedule? _currentSchedule;

        public MainWindow()
        {
            InitializeComponent();
            InitializeEngine();
            InitializeServices();
            LoadDefaultConfiguration();
            LoadDefaultConstraints();
            SetupConverters();
        }

        private void TestOrTools()
        {
            try
            {
                var model = new OrTools.CpModel();
                var x = model.NewIntVar(0, 10, "x");
                var y = model.NewIntVar(0, 10, "y");
                model.Add(x + y == 5);

                var solver = new OrTools.CpSolver();
                var status = solver.Solve(model);

                if (status == OrTools.CpSolverStatus.Optimal)
                {
                    MessageBox.Show($"OR-Tools funziona!\nSoluzione: x={solver.Value(x)}, y={solver.Value(y)}",
                                  "Test OK", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore OR-Tools: {ex.Message}", "Errore",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void InitializeServices()
        {
            _importService = new ExcelImportService();
        }

        private void SetupConverters()
        {
            // Converter già definito nel XAML, non serve aggiungerlo qui
        }

        private void LoadDefaultConstraints()
        {
            try
            {
                _constraints = ConstraintsPersistence.LoadConstraints("default_constraints.json");
                if (_constraints.Any())
                {
                    txtStatus.Text = $"Caricati {_constraints.Count} vincoli salvati";
                }
            }
            catch
            {
                _constraints = new List<Constraint>();
            }
        }

        private void Constraints_Click(object sender, RoutedEventArgs e)
        {
            if (_teachers == null || !_teachers.Any())
            {
                MessageBox.Show("Importa prima i dati da Excel", "Attenzione",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var constraintsWindow = new Views.ConstraintsEditorWindow(
                _teachers, _classes, _constraints, _configuration);

            if (constraintsWindow.ShowDialog() == true)
            {
                _constraints = constraintsWindow.Constraints;
                txtStatus.Text = $"Definiti {_constraints.Count} vincoli";

                // Salva automaticamente i vincoli
                try
                {
                    ConstraintsPersistence.SaveConstraints(_constraints, "default_constraints.json");
                }
                catch { }
            }
        }

        private void LoadDefaultConfiguration()
        {
            try
            {
                _configuration = ScheduleConfiguration.LoadFromFile("default_config.json");
            }
            catch
            {
                _configuration = new ScheduleConfiguration();
            }
        }

        private void InitializeEngine()
        {
            try
            {
                _engine = new EngineInterop();
                bool initialized = _engine.InitializeEngine("config.json");

                if (initialized)
                {
                    txtStatus.Text = "Engine initialized successfully";
                    btnGenerate.IsEnabled = true;
                }
                else
                {
                    txtStatus.Text = $"Engine initialization failed: {_engine.GetLastError()}";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load engine: {ex.Message}",
                              "Initialization Error",
                              MessageBoxButton.OK,
                              MessageBoxImage.Error);
                txtStatus.Text = "Engine not available";
            }
        }

        private void TestEngine_Click(object sender, RoutedEventArgs e)
        {
            TestOrTools();

            if (_engine != null)
            {
                bool result = _engine.GenerateSchedule();
                string message = _engine.GetLastError();

                MessageBox.Show($"Test Result: {result}\nMessage: {message}",
                              "Engine Test",
                              MessageBoxButton.OK,
                              result ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
        }

        private async void ImportExcel_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Excel Files|*.xlsx;*.xlsm;*.xls|All Files|*.*",
                Title = "Seleziona il file Excel con le attività"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    btnImport.IsEnabled = false;
                    txtStatus.Text = "Importazione in corso...";

                    var sheetName = txtSheetName.Text;
                    var result = await _importService.ImportFromExcelAsync(openFileDialog.FileName, sheetName);

                    if (result.Success)
                    {
                        _allActivities = result.Activities;
                        _teachers = result.Teachers;
                        _classes = result.Classes;

                        // Aggiorna le griglie
                        dgActivities.ItemsSource = _allActivities;
                        dgTeachers.ItemsSource = _teachers;
                        dgClasses.ItemsSource = _classes;

                        // Aggiorna contatori
                        lblActivityCount.Content = _allActivities.Count.ToString();
                        txtStats.Text = $"Docenti: {_teachers.Count} | Classi: {_classes.Count}";

                        txtStatus.Text = result.Message;

                        // Mostra eventuali warning
                        if (result.Warnings.Any())
                        {
                            var warningMessage = string.Join("\n", result.Warnings.Take(10));
                            if (result.Warnings.Count > 10)
                                warningMessage += $"\n... e altri {result.Warnings.Count - 10} avvisi";

                            MessageBox.Show(warningMessage, "Avvisi durante l'importazione",
                                          MessageBoxButton.OK, MessageBoxImage.Warning);
                        }

                        // Vai al tab Activities
                        tabControl.SelectedIndex = 0;
                    }
                    else
                    {
                        MessageBox.Show(result.Message, "Errore importazione",
                                      MessageBoxButton.OK, MessageBoxImage.Error);
                        txtStatus.Text = "Importazione fallita";
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Errore: {ex.Message}", "Errore",
                                  MessageBoxButton.OK, MessageBoxImage.Error);
                    txtStatus.Text = "Errore durante l'importazione";
                }
                finally
                {
                    btnImport.IsEnabled = true;
                }
            }
        }

        private void FilterActivities_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_allActivities == null || !_allActivities.Any())
                return;

            var filterText = txtFilter.Text?.ToLower() ?? "";

            if (string.IsNullOrWhiteSpace(filterText))
            {
                dgActivities.ItemsSource = _allActivities;
            }
            else
            {
                var filtered = _allActivities.Where(a =>
                    a.TeacherFullName.ToLower().Contains(filterText) ||
                    a.ClassName.ToLower().Contains(filterText) ||
                    a.Subject.ToLower().Contains(filterText) ||
                    a.ClassCode.ToLower().Contains(filterText)
                ).ToList();

                dgActivities.ItemsSource = filtered;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _engine?.Dispose();
        }

        // Aggiungi questo metodo nella classe MainWindow
        private void Configuration_Click(object sender, RoutedEventArgs e)
        {
            var configWindow = new Views.ConfigurationWindow(_configuration);
            if (configWindow.ShowDialog() == true)
            {
                _configuration = configWindow.Configuration;
                txtStatus.Text = "Configurazione aggiornata";

                // Salva automaticamente in un file di default
                try
                {
                    _configuration.SaveToFile("default_config.json");
                }
                catch { }
            }
        }

        private async void GenerateSchedule_Click(object sender, RoutedEventArgs e)
        {
            // Verifica prerequisiti
            if (_allActivities == null || !_allActivities.Any())
            {
                MessageBox.Show("Importa prima i dati da Excel", "Attenzione",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // NUOVO: Debug dei dati
            var debugResult = MessageBox.Show(
                "Vuoi vedere l'analisi dei dati prima di generare?",
                "Debug",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (debugResult == MessageBoxResult.Yes)
            {
                var debugGenerator = new Services.ScheduleGenerator(
                    _allActivities, _teachers, _classes, _constraints, _configuration);
                debugGenerator.DebugDataConsistency();

                // NUOVO: Aggiungi anche la diagnosi di infeasibility
                var diagResult = MessageBox.Show(
                    "Vuoi vedere la diagnosi di fattibilità?",
                    "Diagnosi",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (diagResult == MessageBoxResult.Yes)
                {
                    debugGenerator.DiagnoseInfeasibility();
                }

                // NUOVO: Analisi specifica vincolo min ore
                var minHoursResult = MessageBox.Show(
                    "Vuoi vedere l'analisi del vincolo min ore docenti?",
                    "Analisi Min Ore",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (minHoursResult == MessageBoxResult.Yes)
                {
                    debugGenerator.AnalyzeMinHoursInfeasibility();
                }
            }

            // Verifica coerenza base
            int maxSlotsPerClass = _configuration.GetActiveDays().Count * _configuration.MaxDailyHours;
            var problematicClasses = _classes.Where(c => c.TotalWeeklyHours > maxSlotsPerClass).ToList();

            if (problematicClasses.Any())
            {
                var classList = string.Join("\n", problematicClasses.Select(c =>
                    $"- {c.Name}: {c.TotalWeeklyHours} ore richieste, max {maxSlotsPerClass} possibili"));

                var continueAnyway = MessageBox.Show(
                    $"ATTENZIONE: Alcune classi hanno più ore di quelle possibili:\n\n{classList}\n\n" +
                    $"Vuoi continuare comunque? (La generazione potrebbe fallire)",
                    "Problema Rilevato",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (continueAnyway != MessageBoxResult.Yes)
                    return;
            }

            // Chiedi conferma
            var result = MessageBox.Show(
                $"Generare l'orario per:\n" +
                $"- {_classes.Count} classi\n" +
                $"- {_teachers.Count} docenti\n" +
                $"- {_allActivities.Count} attività\n\n" +
                $"Tempo stimato: 30 secondi - 2 minuti",
                "Conferma Generazione",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            // Mostra finestra progresso
            var progressWindow = new Views.GenerationProgressWindow();
            var cts = new CancellationTokenSource();
            progressWindow.SetCancellationTokenSource(cts);

            // Crea generatore
            var generator = new Services.ScheduleGenerator(
                _allActivities, _teachers, _classes, _constraints, _configuration);

            // Sottoscrivi eventi progresso
            generator.ProgressChanged += (s, progress) =>
            {
                progressWindow.UpdateProgress(progress);
            };

            // Opzioni di generazione
            var options = new Services.ScheduleGenerator.GenerationOptions
            {
                MaxSeconds = 60,  // 1 minuto max
                OptimizeGaps = true,
                UseParallelProcessing = true
            };

            // Genera in background
            GeneratedSchedule? schedule = null;
            var generateTask = Task.Run(async () =>
            {
                schedule = await generator.GenerateScheduleAsync(options, cts.Token);
            });

            // Mostra dialog progresso
            progressWindow.Owner = this;
            progressWindow.ShowDialog();

            // Aspetta completamento
            await generateTask;

            // Verifica risultato
            if (schedule != null && schedule.IsValid)
            {
                _currentSchedule = schedule;
                DisplaySchedule(schedule);

                txtStatus.Text = $"Orario generato in {schedule.GenerationTime.TotalSeconds:F1}s | " +
                                $"Score: {schedule.Statistics.OptimizationScore:F0}/100 | " +
                                $"Ore buche totali: {schedule.Statistics.TotalTeacherGaps}";
            }
            else
            {
                MessageBox.Show("Generazione fallita. Verifica i vincoli.", "Errore",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Metodo per visualizzare l'orario generato
        private void DisplaySchedule(GeneratedSchedule schedule)
        {
            _currentSchedule = schedule;

            // Mostra statistiche
            var stats = schedule.Statistics;
            var message = $"Orario generato con successo!\n\n" +
                         $"Statistiche:\n" +
                         $"- Slot totali: {stats.TotalSlots}\n" +
                         $"- Ore buche docenti: {stats.TotalTeacherGaps}\n" +
                         $"- Score ottimizzazione: {stats.OptimizationScore:F0}/100\n" +
                         $"- Tempo generazione: {schedule.GenerationTime.TotalSeconds:F1} secondi";

            MessageBox.Show(message, "Generazione Completata",
                          MessageBoxButton.OK, MessageBoxImage.Information);

            // Vai al tab Schedule View
            tabControl.SelectedIndex = 4; // Assumendo che Schedule View sia il 5° tab (index 4)

            // Popola il visualizzatore
            PopulateScheduleViewer();
        }

        private void PopulateScheduleViewer()
        {
            if (_currentSchedule == null) return;

            // Popola il combo selector
            cmbScheduleSelector.Items.Clear();

            if (rbViewClass.IsChecked == true)
            {
                foreach (var cls in _classes.OrderBy(c => c.Name))
                {
                    cmbScheduleSelector.Items.Add(cls.Name);
                }
            }
            else
            {
                foreach (var teacher in _teachers.OrderBy(t => t.FullName))
                {
                    cmbScheduleSelector.Items.Add(teacher.FullName);
                }
            }

            if (cmbScheduleSelector.Items.Count > 0)
            {
                cmbScheduleSelector.SelectedIndex = 0;
            }
        }

        private void ViewModeChanged(object sender, RoutedEventArgs e)
        {
            PopulateScheduleViewer();
        }

        private void ScheduleSelector_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_currentSchedule == null || cmbScheduleSelector.SelectedItem == null) return;

            string selected = cmbScheduleSelector.SelectedItem.ToString();

            if (rbViewClass.IsChecked == true)
            {
                DisplayClassSchedule(selected);
            }
            else
            {
                DisplayTeacherSchedule(selected);
            }
        }

        private void DisplayClassSchedule(string className)
        {
            if (_currentSchedule == null) return;

            var days = _configuration.GetActiveDays();
            var maxHours = _configuration.MaxDailyHours;

            // Pulisci griglia
            grdSchedule.Children.Clear();
            grdSchedule.RowDefinitions.Clear();
            grdSchedule.ColumnDefinitions.Clear();

            // Crea intestazioni colonne (giorni)
            grdSchedule.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) }); // Ora
            foreach (var day in days)
            {
                grdSchedule.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            }

            // Crea righe (ore + header)
            grdSchedule.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            for (int h = 1; h <= maxHours; h++)
            {
                grdSchedule.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) });
            }

            // Header vuoto top-left
            var cornerHeader = new Border
            {
                Background = new SolidColorBrush(Colors.LightGray),
                BorderBrush = new SolidColorBrush(Colors.Gray),
                BorderThickness = new Thickness(1)
            };
            Grid.SetRow(cornerHeader, 0);
            Grid.SetColumn(cornerHeader, 0);
            grdSchedule.Children.Add(cornerHeader);

            // Headers giorni
            int colIndex = 1;
            foreach (var day in days)
            {
                var dayHeader = new Border
                {
                    Background = new SolidColorBrush(Colors.LightBlue),
                    BorderBrush = new SolidColorBrush(Colors.Gray),
                    BorderThickness = new Thickness(1)
                };
                var dayLabel = new TextBlock
                {
                    Text = GetItalianDayName(day),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.Bold
                };
                dayHeader.Child = dayLabel;
                Grid.SetRow(dayHeader, 0);
                Grid.SetColumn(dayHeader, colIndex);
                grdSchedule.Children.Add(dayHeader);
                colIndex++;
            }

            // Headers ore
            for (int h = 1; h <= maxHours; h++)
            {
                var hourHeader = new Border
                {
                    Background = new SolidColorBrush(Colors.LightYellow),
                    BorderBrush = new SolidColorBrush(Colors.Gray),
                    BorderThickness = new Thickness(1)
                };
                var hourLabel = new TextBlock
                {
                    Text = $"{h}° ora",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.Bold
                };
                hourHeader.Child = hourLabel;
                Grid.SetRow(hourHeader, h);
                Grid.SetColumn(hourHeader, 0);
                grdSchedule.Children.Add(hourHeader);
            }

            // Ottieni l'orario della classe
            var classSlots = _currentSchedule.GetClassSchedule(className);

            // Riempi le celle con le lezioni
            foreach (var slot in classSlots)
            {
                int dayIndex = days.IndexOf(slot.Day);
                if (dayIndex < 0 || slot.Hour < 1 || slot.Hour > maxHours) continue;

                var lessonBorder = new Border
                {
                    Background = new SolidColorBrush(GetSubjectColor(slot.Subject)),
                    BorderBrush = new SolidColorBrush(Colors.DarkGray),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(1)
                };

                var lessonPanel = new StackPanel
                {
                    Margin = new Thickness(3)
                };

                lessonPanel.Children.Add(new TextBlock
                {
                    Text = slot.Subject,
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap
                });

                lessonPanel.Children.Add(new TextBlock
                {
                    Text = slot.TeacherName,
                    FontSize = 10,
                    FontStyle = FontStyles.Italic
                });

                if (!string.IsNullOrWhiteSpace(slot.ArticulationGroup))
                {
                    lessonPanel.Children.Add(new TextBlock
                    {
                        Text = $"[{slot.ArticulationGroup}]",
                        FontSize = 9,
                        Foreground = new SolidColorBrush(Colors.Red)
                    });
                }

                lessonBorder.Child = lessonPanel;
                Grid.SetRow(lessonBorder, slot.Hour);
                Grid.SetColumn(lessonBorder, dayIndex + 1);
                grdSchedule.Children.Add(lessonBorder);
            }

            // Celle vuote
            for (int h = 1; h <= maxHours; h++)
            {
                for (int d = 0; d < days.Count; d++)
                {
                    if (!classSlots.Any(s => s.Hour == h && s.Day == days[d]))
                    {
                        var emptyCell = new Border
                        {
                            Background = new SolidColorBrush(Colors.White),
                            BorderBrush = new SolidColorBrush(Colors.LightGray),
                            BorderThickness = new Thickness(0.5)
                        };
                        Grid.SetRow(emptyCell, h);
                        Grid.SetColumn(emptyCell, d + 1);
                        grdSchedule.Children.Add(emptyCell);
                    }
                }
            }
        }

        private void DisplayTeacherSchedule(string teacherName)
        {
            if (_currentSchedule == null) return;

            var days = _configuration.GetActiveDays();
            var maxHours = _configuration.MaxDailyHours;

            // Pulisci griglia
            grdSchedule.Children.Clear();
            grdSchedule.RowDefinitions.Clear();
            grdSchedule.ColumnDefinitions.Clear();

            // Crea intestazioni colonne (giorni)
            grdSchedule.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) }); // Ora
            foreach (var day in days)
            {
                grdSchedule.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            }

            // Crea righe (ore + header)
            grdSchedule.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            for (int h = 1; h <= maxHours; h++)
            {
                grdSchedule.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) });
            }

            // Header vuoto top-left
            var cornerHeader = new Border
            {
                Background = new SolidColorBrush(Colors.LightGray),
                BorderBrush = new SolidColorBrush(Colors.Gray),
                BorderThickness = new Thickness(1)
            };
            Grid.SetRow(cornerHeader, 0);
            Grid.SetColumn(cornerHeader, 0);
            grdSchedule.Children.Add(cornerHeader);

            // Headers giorni
            int colIndex = 1;
            foreach (var day in days)
            {
                var dayHeader = new Border
                {
                    Background = new SolidColorBrush(Colors.LightBlue),
                    BorderBrush = new SolidColorBrush(Colors.Gray),
                    BorderThickness = new Thickness(1)
                };
                var dayLabel = new TextBlock
                {
                    Text = GetItalianDayName(day),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.Bold
                };
                dayHeader.Child = dayLabel;
                Grid.SetRow(dayHeader, 0);
                Grid.SetColumn(dayHeader, colIndex);
                grdSchedule.Children.Add(dayHeader);
                colIndex++;
            }

            // Headers ore
            for (int h = 1; h <= maxHours; h++)
            {
                var hourHeader = new Border
                {
                    Background = new SolidColorBrush(Colors.LightYellow),
                    BorderBrush = new SolidColorBrush(Colors.Gray),
                    BorderThickness = new Thickness(1)
                };
                var hourLabel = new TextBlock
                {
                    Text = $"{h}° ora",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.Bold
                };
                hourHeader.Child = hourLabel;
                Grid.SetRow(hourHeader, h);
                Grid.SetColumn(hourHeader, 0);
                grdSchedule.Children.Add(hourHeader);
            }

            // Ottieni l'orario del docente
            var teacherSlots = _currentSchedule.GetTeacherSchedule(teacherName);

            // Riempi le celle con le lezioni del docente
            foreach (var slot in teacherSlots)
            {
                int dayIndex = days.IndexOf(slot.Day);
                if (dayIndex < 0 || slot.Hour < 1 || slot.Hour > maxHours) continue;

                var lessonBorder = new Border
                {
                    Background = new SolidColorBrush(GetSubjectColor(slot.ClassName)), // Colore per classe invece che materia
                    BorderBrush = new SolidColorBrush(Colors.DarkGray),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(1)
                };

                var lessonPanel = new StackPanel
                {
                    Margin = new Thickness(3)
                };

                // Per il docente mostriamo: Classe, Materia
                lessonPanel.Children.Add(new TextBlock
                {
                    Text = slot.ClassName,  // Mostra la classe
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap
                });

                lessonPanel.Children.Add(new TextBlock
                {
                    Text = slot.Subject,  // Mostra la materia
                    FontSize = 10,
                    FontStyle = FontStyles.Italic
                });

                if (!string.IsNullOrWhiteSpace(slot.ArticulationGroup))
                {
                    lessonPanel.Children.Add(new TextBlock
                    {
                        Text = $"[{slot.ArticulationGroup}]",
                        FontSize = 9,
                        Foreground = new SolidColorBrush(Colors.Red)
                    });
                }

                lessonBorder.Child = lessonPanel;
                Grid.SetRow(lessonBorder, slot.Hour);
                Grid.SetColumn(lessonBorder, dayIndex + 1);
                grdSchedule.Children.Add(lessonBorder);
            }

            // Celle vuote - con evidenziazione ore buche
            for (int h = 1; h <= maxHours; h++)
            {
                for (int d = 0; d < days.Count; d++)
                {
                    if (!teacherSlots.Any(s => s.Hour == h && s.Day == days[d]))
                    {
                        // Controlla se è un'ora buca (tra due lezioni)
                        bool isGap = false;
                        var daySlots = teacherSlots.Where(s => s.Day == days[d]).OrderBy(s => s.Hour).ToList();
                        if (daySlots.Count >= 2)
                        {
                            var firstHour = daySlots.First().Hour;
                            var lastHour = daySlots.Last().Hour;
                            if (h > firstHour && h < lastHour)
                            {
                                isGap = true;
                            }
                        }

                        var emptyCell = new Border
                        {
                            Background = new SolidColorBrush(isGap ? Colors.MistyRose : Colors.White),
                            BorderBrush = new SolidColorBrush(Colors.LightGray),
                            BorderThickness = new Thickness(0.5)
                        };

                        if (isGap)
                        {
                            // Aggiungi testo per indicare ora buca
                            var gapText = new TextBlock
                            {
                                Text = "BUCA",
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center,
                                Foreground = new SolidColorBrush(Colors.Red),
                                FontSize = 9
                            };
                            emptyCell.Child = gapText;
                        }

                        Grid.SetRow(emptyCell, h);
                        Grid.SetColumn(emptyCell, d + 1);
                        grdSchedule.Children.Add(emptyCell);
                    }
                }
            }

            // Mostra statistiche del docente nella status bar
            var teacherGaps = _currentSchedule.Statistics.TeacherGaps.ContainsKey(teacherName)
                ? _currentSchedule.Statistics.TeacherGaps[teacherName]
                : 0;
            var teacherHours = teacherSlots.Count;

            txtStatus.Text = $"Docente: {teacherName} | Ore settimanali: {teacherHours} | Ore buche: {teacherGaps}";
        }

        private Color GetSubjectColor(string subject)
        {
            // Assegna colori diversi alle materie
            var hash = subject.GetHashCode();
            var colors = new[]
            {
        Colors.LightBlue, Colors.LightGreen, Colors.LightYellow,
        Colors.LightPink, Colors.LightCyan, Colors.LightSalmon,
        Colors.LightSteelBlue, Colors.LightGoldenrodYellow
    };
            return colors[Math.Abs(hash) % colors.Length];
        }

        private string GetItalianDayName(DayOfWeek day)
        {
            return day switch
            {
                DayOfWeek.Monday => "Lunedì",
                DayOfWeek.Tuesday => "Martedì",
                DayOfWeek.Wednesday => "Mercoledì",
                DayOfWeek.Thursday => "Giovedì",
                DayOfWeek.Friday => "Venerdì",
                DayOfWeek.Saturday => "Sabato",
                _ => day.ToString()
            };
        }

        private void ExportSchedule_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Export Excel in sviluppo", "Info",
                          MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void PrintSchedule_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Stampa in sviluppo", "Info",
                          MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // Converter per visualizzare HashSet come stringa
    public class SetToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter,
                            System.Globalization.CultureInfo culture)
        {
            if (value is HashSet<string> set)
            {
                return string.Join(", ", set);
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter,
                                 System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }

    }
}
