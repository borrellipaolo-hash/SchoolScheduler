using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SchoolScheduler.Common.Models;
using Microsoft.Win32;
using System.Text.Json;
using System.IO;

namespace SchoolScheduler.GUI.Views
{
    public partial class ConstraintsEditorWindow : Window
    {
        private List<Constraint> _constraints;
        private List<Teacher> _teachers;
        private List<SchoolClass> _classes;
        private ScheduleConfiguration _configuration;
        private Dictionary<string, CheckBox> _availabilityCheckBoxes;

        public List<Constraint> Constraints => _constraints;

        public ConstraintsEditorWindow(List<Teacher> teachers, List<SchoolClass> classes,
                                      List<Constraint> existingConstraints,
                                      ScheduleConfiguration configuration)
        {
            InitializeComponent();
            _teachers = teachers ?? new List<Teacher>();
            _classes = classes ?? new List<SchoolClass>();
            _constraints = existingConstraints ?? new List<Constraint>();
            _configuration = configuration ?? new ScheduleConfiguration();
            _availabilityCheckBoxes = new Dictionary<string, CheckBox>();

            InitializeUI();
            RefreshConstraintsList();
        }

        private void InitializeUI()
        {
            // Popola combo docenti
            cmbTeacher.Items.Clear();
            foreach (var teacher in _teachers.OrderBy(t => t.FullName))
            {
                cmbTeacher.Items.Add(teacher.FullName);
            }

            // Popola combo classi
            cmbClass.Items.Clear();
            foreach (var schoolClass in _classes.OrderBy(c => c.Name))
            {
                cmbClass.Items.Add(schoolClass.Name);
            }

            // Crea griglia disponibilità
            CreateAvailabilityGrid();

            cmbMonday.SelectionChanged += (s, e) => UpdateTotalHours();
            cmbTuesday.SelectionChanged += (s, e) => UpdateTotalHours();
            cmbWednesday.SelectionChanged += (s, e) => UpdateTotalHours();
            cmbThursday.SelectionChanged += (s, e) => UpdateTotalHours();
            cmbFriday.SelectionChanged += (s, e) => UpdateTotalHours();
            cmbSaturday.SelectionChanged += (s, e) => UpdateTotalHours();
        }

        private void CreateAvailabilityGrid()
        {
            grdAvailability.Children.Clear();
            grdAvailability.RowDefinitions.Clear();
            grdAvailability.ColumnDefinitions.Clear();
            _availabilityCheckBoxes.Clear();

            var days = _configuration.GetActiveDays();
            int maxHours = _configuration.MaxDailyHours;

            // Aggiungi definizioni colonne (giorni + header)
            grdAvailability.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foreach (var day in days)
            {
                grdAvailability.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            }

            // Aggiungi definizioni righe (ore + header)
            grdAvailability.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int hour = 1; hour <= maxHours; hour++)
            {
                grdAvailability.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            // Header vuoto top-left
            var emptyHeader = new Label();
            Grid.SetRow(emptyHeader, 0);
            Grid.SetColumn(emptyHeader, 0);
            grdAvailability.Children.Add(emptyHeader);

            // Headers giorni
            int colIndex = 1;
            foreach (var day in days)
            {
                var dayHeader = new Label
                {
                    Content = GetItalianDayName(day),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontWeight = FontWeights.Bold
                };
                Grid.SetRow(dayHeader, 0);
                Grid.SetColumn(dayHeader, colIndex);
                grdAvailability.Children.Add(dayHeader);
                colIndex++;
            }

            // Headers ore e checkbox
            for (int hour = 1; hour <= maxHours; hour++)
            {
                // Header ora
                var hourHeader = new Label
                {
                    Content = $"{hour}° ora",
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.Bold
                };
                Grid.SetRow(hourHeader, hour);
                Grid.SetColumn(hourHeader, 0);
                grdAvailability.Children.Add(hourHeader);

                // Checkbox per ogni giorno
                colIndex = 1;
                foreach (var day in days)
                {
                    var checkBox = new CheckBox
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        ToolTip = $"Non disponibile {GetItalianDayName(day)} {hour}° ora"
                    };

                    string key = $"{day}_{hour}";
                    _availabilityCheckBoxes[key] = checkBox;

                    Grid.SetRow(checkBox, hour);
                    Grid.SetColumn(checkBox, colIndex);
                    grdAvailability.Children.Add(checkBox);
                    colIndex++;
                }
            }
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
                DayOfWeek.Sunday => "Domenica",
                _ => day.ToString()
            };
        }

        private void RefreshConstraintsList()
        {
            lstConstraints.ItemsSource = null;
            lstConstraints.ItemsSource = _constraints;
        }

        private void TeacherSelected(object sender, SelectionChangedEventArgs e)
        {
            // Abilita i controlli quando un docente è selezionato
            btnAddAvailability.IsEnabled = cmbTeacher.SelectedItem != null;
            btnAddMaxDaily.IsEnabled = cmbTeacher.SelectedItem != null;
            btnAddGapsConstraint.IsEnabled = cmbTeacher.SelectedItem != null;
            btnAddDayOff.IsEnabled = cmbTeacher.SelectedItem != null;
        }

        private void AddAvailabilityConstraint_Click(object sender, RoutedEventArgs e)
        {
            if (cmbTeacher.SelectedItem == null)
            {
                MessageBox.Show("Seleziona un docente", "Attenzione",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var teacherName = cmbTeacher.SelectedItem.ToString();
            var unavailableSlots = new List<TimeSlot>();

            foreach (var kvp in _availabilityCheckBoxes)
            {
                if (kvp.Value.IsChecked == true)
                {
                    var parts = kvp.Key.Split('_');
                    if (Enum.TryParse<DayOfWeek>(parts[0], out var day) &&
                        int.TryParse(parts[1], out var hour))
                    {
                        unavailableSlots.Add(new TimeSlot { Day = day, Hour = hour });
                    }
                }
            }

            if (!unavailableSlots.Any())
            {
                MessageBox.Show("Seleziona almeno uno slot non disponibile", "Attenzione",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var constraint = new TeacherAvailabilityConstraint
            {
                TeacherName = teacherName,
                UnavailableSlots = unavailableSlots,
                Name = $"Disponibilità {teacherName}"
            };

            _constraints.Add(constraint);
            RefreshConstraintsList();

            // Reset checkboxes
            foreach (var cb in _availabilityCheckBoxes.Values)
            {
                cb.IsChecked = false;
            }
        }

        private void AddMaxDailyConstraint_Click(object sender, RoutedEventArgs e)
        {
            if (cmbTeacher.SelectedItem == null) return;

            var teacherName = cmbTeacher.SelectedItem.ToString();
            int.TryParse(cmbMaxDailyHoursTeacher.Text, out int maxHours);

            var priorityItem = cmbMaxDailyPriority.SelectedItem as ComboBoxItem;
            int.TryParse(priorityItem?.Tag?.ToString(), out int priorityValue);
            var priority = (ConstraintPriority)priorityValue;

            var constraint = new TeacherMaxDailyHoursConstraint
            {
                TeacherName = teacherName,
                MaxHours = maxHours,
                Priority = priority,
                Name = $"Max ore giornaliere {teacherName}"
            };

            _constraints.Add(constraint);
            RefreshConstraintsList();
        }

        private void AddGapsConstraint_Click(object sender, RoutedEventArgs e)
        {
            if (cmbTeacher.SelectedItem == null) return;

            var teacherName = cmbTeacher.SelectedItem.ToString();
            int.TryParse(cmbMaxWeeklyGaps.Text, out int maxGaps);

            var priorityItem = cmbGapsPriority.SelectedItem as ComboBoxItem;
            int.TryParse(priorityItem?.Tag?.ToString(), out int priorityValue);
            var priority = (ConstraintPriority)priorityValue;

            var constraint = new TeacherMaxWeeklyGapsConstraint
            {
                TeacherName = teacherName,
                MaxGaps = maxGaps,
                Priority = priority,
                Name = $"Max ore buche {teacherName}"
            };

            _constraints.Add(constraint);
            RefreshConstraintsList();
        }

        private void AddDayOff_Click(object sender, RoutedEventArgs e)
        {
            if (cmbTeacher.SelectedItem == null || cmbDayOff.SelectedItem == null) return;

            var teacherName = cmbTeacher.SelectedItem.ToString();
            var dayItem = cmbDayOff.SelectedItem as ComboBoxItem;

            if (Enum.TryParse<DayOfWeek>(dayItem?.Tag?.ToString(), out var day))
            {
                var priorityItem = cmbDayOffPriority.SelectedItem as ComboBoxItem;
                int.TryParse(priorityItem?.Tag?.ToString(), out int priorityValue);
                var priority = (ConstraintPriority)priorityValue;

                var constraint = new TeacherDayOffConstraint
                {
                    TeacherName = teacherName,
                    DayOff = day,
                    Priority = priority,
                    Name = $"Giorno libero {teacherName}"
                };

                _constraints.Add(constraint);
                RefreshConstraintsList();
            }
        }

        private void ConstraintSelected(object sender, SelectionChangedEventArgs e)
        {
            btnRemoveConstraint.IsEnabled = lstConstraints.SelectedItem != null;
        }

        private void RemoveConstraint_Click(object sender, RoutedEventArgs e)
        {
            if (lstConstraints.SelectedItem is Constraint constraint)
            {
                _constraints.Remove(constraint);
                RefreshConstraintsList();
            }
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Rimuovere tutti i vincoli?", "Conferma",
                              MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _constraints.Clear();
                RefreshConstraintsList();
            }
        }

        private void ParseAdvanced_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Parser avanzato in sviluppo", "Info",
                          MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SaveConstraints_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "JSON Files|*.json|All Files|*.*",
                DefaultExt = "json",
                FileName = "constraints.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    ConstraintsPersistence.SaveConstraints(_constraints, dialog.FileName);
                    MessageBox.Show("Vincoli salvati con successo", "Successo",
                                  MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Errore nel salvataggio: {ex.Message}", "Errore",
                                  MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void LoadConstraints_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JSON Files|*.json|All Files|*.*",
                DefaultExt = "json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var loadedConstraints = ConstraintsPersistence.LoadConstraints(dialog.FileName);

                    if (MessageBox.Show($"Trovati {loadedConstraints.Count} vincoli. Vuoi sostituire i vincoli attuali?",
                                      "Conferma caricamento",
                                      MessageBoxButton.YesNo,
                                      MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        _constraints.Clear();
                        _constraints.AddRange(loadedConstraints);
                        RefreshConstraintsList();

                        MessageBox.Show($"Caricati {loadedConstraints.Count} vincoli", "Successo",
                                      MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Errore nel caricamento: {ex.Message}", "Errore",
                                  MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ClassSelected(object sender, SelectionChangedEventArgs e)
        {
            btnAddClassDailyHours.IsEnabled = cmbClass.SelectedItem != null;
            btnAddWeeklyDistribution.IsEnabled = cmbClass.SelectedItem != null;

            if (cmbClass.SelectedItem != null)
            {
                var className = cmbClass.SelectedItem.ToString();
                var schoolClass = _classes.FirstOrDefault(c => c.Name == className);
                if (schoolClass != null)
                {
                    txtClassInfo.Text = $"Ore totali settimanali: {schoolClass.TotalWeeklyHours}";
                    UpdateTotalHours();
                }
            }
        }

        private void AddClassDailyHours_Click(object sender, RoutedEventArgs e)
        {
            if (cmbClass.SelectedItem == null || cmbClassDay.SelectedItem == null)
            {
                MessageBox.Show("Seleziona classe e giorno", "Attenzione",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var className = cmbClass.SelectedItem.ToString();
            var dayItem = cmbClassDay.SelectedItem as ComboBoxItem;
            int.TryParse(cmbClassHours.Text, out int hours);

            if (Enum.TryParse<DayOfWeek>(dayItem?.Tag?.ToString(), out var day))
            {
                var constraint = new ClassDailyHoursConstraint
                {
                    ClassName = className,
                    Day = day,
                    Hours = hours,
                    Name = $"Ore {className} {day}"
                };

                _constraints.Add(constraint);
                RefreshConstraintsList();

                MessageBox.Show($"Aggiunto vincolo: {className} deve fare {hours} ore il {GetItalianDayName(day)}",
                              "Vincolo Aggiunto", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void AddWeeklyDistribution_Click(object sender, RoutedEventArgs e)
        {
            if (cmbClass.SelectedItem == null)
            {
                MessageBox.Show("Seleziona una classe", "Attenzione",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var className = cmbClass.SelectedItem.ToString();
            var distribution = new Dictionary<DayOfWeek, int>();

            // Raccogli le ore per ogni giorno
            int.TryParse(cmbMonday.Text, out int mon);
            int.TryParse(cmbTuesday.Text, out int tue);
            int.TryParse(cmbWednesday.Text, out int wed);
            int.TryParse(cmbThursday.Text, out int thu);
            int.TryParse(cmbFriday.Text, out int fri);
            int.TryParse(cmbSaturday.Text, out int sat);

            if (mon > 0) distribution[DayOfWeek.Monday] = mon;
            if (tue > 0) distribution[DayOfWeek.Tuesday] = tue;
            if (wed > 0) distribution[DayOfWeek.Wednesday] = wed;
            if (thu > 0) distribution[DayOfWeek.Thursday] = thu;
            if (fri > 0) distribution[DayOfWeek.Friday] = fri;
            if (sat > 0) distribution[DayOfWeek.Saturday] = sat;

            // Verifica che il totale corrisponda
            var totalHours = distribution.Values.Sum();
            var schoolClass = _classes.FirstOrDefault(c => c.Name == className);

            if (schoolClass != null && totalHours != schoolClass.TotalWeeklyHours)
            {
                if (MessageBox.Show($"Il totale ore ({totalHours}) non corrisponde alle ore della classe ({schoolClass.TotalWeeklyHours}). Continuare?",
                                  "Attenzione", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            var constraint = new ClassWeeklyDistributionConstraint
            {
                ClassName = className,
                DailyHours = distribution,
                Name = $"Distribuzione {className}"
            };

            _constraints.Add(constraint);
            RefreshConstraintsList();

            MessageBox.Show($"Aggiunta distribuzione settimanale per {className}",
                          "Vincolo Aggiunto", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void UpdateTotalHours()
        {
            if (!IsLoaded) return;

            int.TryParse(cmbMonday?.Text ?? "0", out int mon);
            int.TryParse(cmbTuesday?.Text ?? "0", out int tue);
            int.TryParse(cmbWednesday?.Text ?? "0", out int wed);
            int.TryParse(cmbThursday?.Text ?? "0", out int thu);
            int.TryParse(cmbFriday?.Text ?? "0", out int fri);
            int.TryParse(cmbSaturday?.Text ?? "0", out int sat);

            var total = mon + tue + wed + thu + fri + sat;

            if (txtTotalHours != null)
                txtTotalHours.Text = $"Totale ore: {total}";
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
