using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Threading;
using OrTools = Google.OrTools.Sat;
using Activity = SchoolScheduler.Common.Models.Activity;
using SchoolScheduler.Common.Models;


namespace SchoolScheduler.GUI.Services
{
    public class ScheduleGenerator
    {
        public class GenerationOptions
        {
            public int MaxSeconds { get; set; } = 30;  // Tempo massimo di generazione
            public bool OptimizeGaps { get; set; } = true;  // Minimizza ore buche
            public bool UseParallelProcessing { get; set; } = true;
            public int NumWorkers { get; set; } = 4;  // Thread per OR-Tools
            public bool VerboseLogging { get; set; } = false;
        }

        public class GenerationProgress
        {
            public int Percentage { get; set; }
            public string Message { get; set; } = string.Empty;
            public bool IsCompleted { get; set; }
            public bool HasErrors { get; set; }
        }

        private readonly List<Activity> _activities;
        private readonly List<Teacher> _teachers;
        private readonly List<SchoolClass> _classes;
        private readonly List<Constraint> _constraints;
        private readonly ScheduleConfiguration _config;
        private OrTools.CpModel _model;
        private Dictionary<string, OrTools.IntVar> _variables;

        public event EventHandler<GenerationProgress>? ProgressChanged;

        public ScheduleGenerator(
            List<Activity> activities,
            List<Teacher> teachers,
            List<SchoolClass> classes,
            List<Constraint> constraints,
            ScheduleConfiguration config)
        {
            _activities = activities ?? new List<Activity>();
            _teachers = teachers ?? new List<Teacher>();
            _classes = classes ?? new List<SchoolClass>();
            _constraints = constraints ?? new List<Constraint>();
            _config = config ?? new ScheduleConfiguration();
            _model = new OrTools.CpModel();
            _variables = new Dictionary<string, OrTools.IntVar>();
        }

        public async Task<GeneratedSchedule> GenerateScheduleAsync(
            GenerationOptions options,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => GenerateSchedule(options, cancellationToken), cancellationToken);
        }

        public GeneratedSchedule GenerateSchedule(
            GenerationOptions options,
            CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new GeneratedSchedule();

            try
            {
                ReportProgress(0, "Inizializzazione modello...");

                // 1. Crea variabili
                CreateVariables();
                ReportProgress(20, "Variabili create");

                // 2. Aggiungi vincoli base
                AddBaseConstraints();
                ReportProgress(40, "Vincoli base aggiunti");

                // 3. Aggiungi vincoli utente
                AddUserConstraints();
                ReportProgress(60, "Vincoli utente aggiunti");

                // 4. Aggiungi obiettivo ottimizzazione
                if (options.OptimizeGaps)
                {
                    AddOptimizationObjective();
                }
                ReportProgress(70, "Obiettivo ottimizzazione impostato");

                // 5. Risolvi
                ReportProgress(80, "Risoluzione in corso...");
                var solver = new OrTools.CpSolver();

                // Imposta parametri solver
                solver.StringParameters = $"max_time_in_seconds:{options.MaxSeconds}";
                if (options.UseParallelProcessing)
                {
                    solver.StringParameters += $",num_workers:{options.NumWorkers}";
                }

                // Callback per progresso
                var solutionCallback = new SolutionCallback(this);

                var status = solver.Solve(_model, solutionCallback);

                // 6. Estrai soluzione
                if (status == OrTools.CpSolverStatus.Optimal || status == OrTools.CpSolverStatus.Feasible)
                {
                    result = ExtractSolution(solver);
                    result.IsValid = true;
                    ReportProgress(100, "Orario generato con successo!");
                }
                else
                {
                    result.IsValid = false;
                    result.Warnings.Add($"Impossibile trovare una soluzione: {status}");
                    ReportProgress(100, $"Generazione fallita: {status}", true);
                }
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Warnings.Add($"Errore: {ex.Message}");
                ReportProgress(100, $"Errore: {ex.Message}", true);
            }

            stopwatch.Stop();
            result.GenerationTime = stopwatch.Elapsed;

            // Calcola statistiche
            result.Statistics.CalculateStatistics(result, _teachers);

            return result;
        }

        private void CreateVariables()
        {
            var days = _config.GetActiveDays();
            var maxHours = _config.MaxDailyHours;

            foreach (var activity in _activities)
            {
                // Per ogni attività, crea variabili solo per gli slot dove può INIZIARE
                // Un'attività di N ore che inizia all'ora H occupa gli slot H, H+1, ..., H+N-1

                for (int d = 0; d < days.Count; d++)
                {
                    // Un'attività può iniziare solo se c'è spazio per tutte le sue ore consecutive
                    for (int startHour = 1; startHour <= maxHours - activity.WeeklyHours + 1; startHour++)
                    {
                        // Variabile: l'attività inizia nel giorno d all'ora startHour
                        string varName = $"act_{activity.Id}_d{d}_start{startHour}";
                        var variable = _model.NewBoolVar(varName);
                        _variables[varName] = variable;
                    }
                }
            }
        }

        private void AddBaseConstraints()
        {
            var days = _config.GetActiveDays();
            var maxHours = _config.MaxDailyHours;

            ReportProgress(25, $"Configurazione: {days.Count} giorni, max {maxHours} ore/giorno");

            // DEBUG: Verifica dati
            int totalHoursNeeded = _activities.Sum(a => a.WeeklyHours);
            int totalSlotsAvailable = days.Count * maxHours * _classes.Count;
            ReportProgress(30, $"Ore necessarie: {totalHoursNeeded}, Slot disponibili: {totalSlotsAvailable}");

            // 1. Ogni attività deve essere assegnata ESATTAMENTE UNA VOLTA
            foreach (var activity in _activities)
            {
                var activityVars = new List<OrTools.IntVar>();

                for (int d = 0; d < days.Count; d++)
                {
                    for (int startHour = 1; startHour <= maxHours - activity.WeeklyHours + 1; startHour++)
                    {
                        string varName = $"act_{activity.Id}_d{d}_start{startHour}";
                        if (_variables.ContainsKey(varName))
                        {
                            activityVars.Add(_variables[varName]);
                        }
                    }
                }

                // Ogni attività deve iniziare esattamente una volta
                _model.Add(OrTools.LinearExpr.Sum(activityVars) == 1);
            }
            

            // 2. Un docente può essere in un solo posto alla volta
            foreach (var teacher in _teachers)
            {
                var teacherActivities = _activities.Where(a => a.TeacherFullName == teacher.FullName).ToList();

                for (int d = 0; d < days.Count; d++)
                {
                    for (int h = 1; h <= maxHours; h++)
                    {
                        var conflictVars = new List<OrTools.IntVar>();

                        foreach (var activity in teacherActivities)
                        {
                            // Un'attività occupa l'ora h se inizia in un'ora <= h e finisce in un'ora >= h
                            for (int startHour = Math.Max(1, h - activity.WeeklyHours + 1);
                                 startHour <= Math.Min(h, maxHours - activity.WeeklyHours + 1);
                                 startHour++)
                            {
                                string varName = $"act_{activity.Id}_d{d}_start{startHour}";
                                if (_variables.ContainsKey(varName))
                                {
                                    // Questa attività occupa l'ora h se inizia a startHour
                                    if (h >= startHour && h < startHour + activity.WeeklyHours)
                                    {
                                        conflictVars.Add(_variables[varName]);
                                    }
                                }
                            }
                        }

                        // Al massimo una attività del docente può occupare l'ora h
                        if (conflictVars.Count > 1)
                        {
                            _model.Add(OrTools.LinearExpr.Sum(conflictVars) <= 1);
                        }
                    }
                }
            }
            
            // 3. Una classe può avere una sola lezione alla volta (considerando articolazioni)
            foreach (var schoolClass in _classes)
            {
                // Separa attività normali e articolate
                var normalActivities = _activities
                    .Where(a => a.ClassName == schoolClass.Name && string.IsNullOrWhiteSpace(a.ArticulationGroup))
                    .ToList();

                for (int d = 0; d < days.Count; d++)
                {
                    for (int h = 1; h <= maxHours; h++)
                    {
                        var conflictVars = new List<OrTools.IntVar>();

                        foreach (var activity in normalActivities)
                        {
                            // Controlla se questa attività può occupare l'ora h
                            for (int startHour = Math.Max(1, h - activity.WeeklyHours + 1);
                                 startHour <= Math.Min(h, maxHours - activity.WeeklyHours + 1);
                                 startHour++)
                            {
                                string varName = $"act_{activity.Id}_d{d}_start{startHour}";
                                if (_variables.ContainsKey(varName))
                                {
                                    if (h >= startHour && h < startHour + activity.WeeklyHours)
                                    {
                                        conflictVars.Add(_variables[varName]);
                                    }
                                }
                            }
                        }

                        // Al massimo una attività normale per classe per ora
                        if (conflictVars.Count > 1)
                        {
                            _model.Add(OrTools.LinearExpr.Sum(conflictVars) <= 1);
                        }
                    }
                }
            }
            
            // 4. Stesso docente, stessa classe, stessa materia = giorni diversi
            var groupedActivities = _activities
                .GroupBy(a => new { a.TeacherFullName, a.ClassName, a.Subject })
                .Where(g => g.Count() > 1)
                .ToList();

            ReportProgress(35, $"Trovati {groupedActivities.Count} gruppi di attività da distribuire");

            foreach (var group in groupedActivities)
            {
                var activities = group.ToList();

                // Ogni attività del gruppo deve essere in un giorno diverso
                for (int d = 0; d < days.Count; d++)
                {
                    var dayVars = new List<OrTools.IntVar>();

                    foreach (var activity in activities)
                    {
                        // Tutte le possibili posizioni di inizio per questa attività in questo giorno
                        for (int startHour = 1; startHour <= maxHours - activity.WeeklyHours + 1; startHour++)
                        {
                            string varName = $"act_{activity.Id}_d{d}_start{startHour}";
                            if (_variables.ContainsKey(varName))
                            {
                                dayVars.Add(_variables[varName]);
                            }
                        }
                    }

                    // Al massimo UNA di queste attività può essere in questo giorno
                    if (dayVars.Count > 1)
                    {
                        _model.Add(OrTools.LinearExpr.Sum(dayVars) <= 1);
                    }
                }
            }
            
            // 5. Vincolo MIN/MAX ore giornaliere per le classi
            ReportProgress(40, "Aggiunta vincolo min/max ore per classi...");

            foreach (var schoolClass in _classes)
            {
                var classActivities = _activities.Where(a => a.ClassName == schoolClass.Name).ToList();

                for (int d = 0; d < days.Count; d++)
                {
                    // Conta le ore TOTALI della classe nel giorno (non le attività)
                    var totalHoursInDay = _model.NewIntVar(0, maxHours, $"classHours_{schoolClass.Name}_d{d}");
                    var hourSums = new List<OrTools.LinearExpr>();

                    foreach (var activity in classActivities)
                    {
                        for (int startHour = 1; startHour <= maxHours - activity.WeeklyHours + 1; startHour++)
                        {
                            string varName = $"act_{activity.Id}_d{d}_start{startHour}";
                            if (_variables.ContainsKey(varName))
                            {
                                // Se l'attività inizia qui, aggiunge le sue ore al totale
                                hourSums.Add(_variables[varName] * activity.WeeklyHours);
                            }
                        }
                    }

                    if (hourSums.Count > 0)
                    {
                        _model.Add(totalHoursInDay == OrTools.LinearExpr.Sum(hourSums));

                        // VINCOLO CHIAVE: O fa 0 ore (non viene) O fa tra MIN e MAX
                        var hasSchool = _model.NewBoolVar($"hasSchool_{schoolClass.Name}_d{d}");

                        _model.Add(totalHoursInDay > 0).OnlyEnforceIf(hasSchool);
                        _model.Add(totalHoursInDay == 0).OnlyEnforceIf(hasSchool.Not());

                        // Se viene a scuola, DEVE fare tra min e max ore
                        _model.Add(totalHoursInDay >= _config.MinDailyHours).OnlyEnforceIf(hasSchool);
                        _model.Add(totalHoursInDay <= _config.MaxDailyHours).OnlyEnforceIf(hasSchool);
                    }
                }

                // VINCOLO AGGIUNTIVO: Verifica che il totale settimanale sia corretto
                var allHourSums = new List<OrTools.LinearExpr>();
                foreach (var activity in classActivities)
                {
                    for (int d = 0; d < days.Count; d++)
                    {
                        for (int startHour = 1; startHour <= maxHours - activity.WeeklyHours + 1; startHour++)
                        {
                            string varName = $"act_{activity.Id}_d{d}_start{startHour}";
                            if (_variables.ContainsKey(varName))
                            {
                                allHourSums.Add(_variables[varName] * activity.WeeklyHours);
                            }
                        }
                    }
                }

                if (allHourSums.Count > 0)
                {
                    var totalWeeklyHours = classActivities.Sum(a => a.WeeklyHours);
                    _model.Add(OrTools.LinearExpr.Sum(allHourSums) == totalWeeklyHours);
                }
            }
            
            // 6. Le classi devono iniziare dalla prima ora
            ReportProgress(42, "Aggiunta vincolo ingresso prima ora...");

            foreach (var schoolClass in _classes)
            {
                var classActivities = _activities.Where(a => a.ClassName == schoolClass.Name).ToList();

                for (int d = 0; d < days.Count; d++)
                {
                    // Se la classe ha lezioni in un giorno, almeno una deve iniziare alla prima ora
                    var firstHourStarts = new List<OrTools.IntVar>();
                    var anyActivityInDay = new List<OrTools.IntVar>();

                    foreach (var activity in classActivities)
                    {
                        // Variabili per attività che iniziano alla prima ora
                        string firstHourVar = $"act_{activity.Id}_d{d}_start1";
                        if (_variables.ContainsKey(firstHourVar))
                        {
                            firstHourStarts.Add(_variables[firstHourVar]);
                        }

                        // Tutte le attività del giorno
                        for (int startHour = 1; startHour <= maxHours - activity.WeeklyHours + 1; startHour++)
                        {
                            string varName = $"act_{activity.Id}_d{d}_start{startHour}";
                            if (_variables.ContainsKey(varName))
                            {
                                anyActivityInDay.Add(_variables[varName]);
                            }
                        }
                    }

                    if (firstHourStarts.Count > 0 && anyActivityInDay.Count > 0)
                    {
                        var hasAnyLesson = _model.NewBoolVar($"hasAny_{schoolClass.Name}_d{d}");
                        var hasFirstHour = _model.NewBoolVar($"hasFirst_{schoolClass.Name}_d{d}");

                        _model.Add(OrTools.LinearExpr.Sum(anyActivityInDay) > 0).OnlyEnforceIf(hasAnyLesson);
                        _model.Add(OrTools.LinearExpr.Sum(anyActivityInDay) == 0).OnlyEnforceIf(hasAnyLesson.Not());

                        _model.Add(OrTools.LinearExpr.Sum(firstHourStarts) > 0).OnlyEnforceIf(hasFirstHour);
                        _model.Add(OrTools.LinearExpr.Sum(firstHourStarts) == 0).OnlyEnforceIf(hasFirstHour.Not());

                        // Se c'è lezione, deve iniziare dalla prima ora
                        _model.AddImplication(hasAnyLesson, hasFirstHour);
                    }
                }
            }
            
            // 7. Vincolo max ore giornaliere per TUTTI i docenti
            ReportProgress(41, "Aggiunta vincolo max ore giornaliere docenti...");
            int maxDailyHoursForTeachers = 5; // Configurabile

            foreach (var teacher in _teachers)
            {
                var teacherActivities = _activities.Where(a => a.TeacherFullName == teacher.FullName).ToList();

                for (int d = 0; d < days.Count; d++)
                {
                    // Conta le ore totali del docente nel giorno
                    var hoursInDay = _model.NewIntVar(0, maxHours, $"teacherHours_{teacher.FullName}_d{d}");
                    var hourSums = new List<OrTools.LinearExpr>();

                    foreach (var activity in teacherActivities)
                    {
                        for (int startHour = 1; startHour <= maxHours - activity.WeeklyHours + 1; startHour++)
                        {
                            string varName = $"act_{activity.Id}_d{d}_start{startHour}";
                            if (_variables.ContainsKey(varName))
                            {
                                // Se l'attività inizia qui, aggiunge WeeklyHours ore al totale
                                hourSums.Add(_variables[varName] * activity.WeeklyHours);
                            }
                        }
                    }

                    if (hourSums.Count > 0)
                    {
                        _model.Add(hoursInDay == OrTools.LinearExpr.Sum(hourSums));
                        _model.Add(hoursInDay <= maxDailyHoursForTeachers);
                    }
                }
            }
            
            // 8. NO BUCHI per le classi (ore consecutive nella giornata)
            ReportProgress(43, "Aggiunta vincolo no buchi per classi...");

            foreach (var schoolClass in _classes)
            {
                var classActivities = _activities.Where(a => a.ClassName == schoolClass.Name).ToList();

                for (int d = 0; d < days.Count; d++)
                {
                    // Per ogni ora dal 2 alla penultima
                    for (int h = 2; h < maxHours; h++)
                    {
                        // Variabili per ora precedente, corrente e successiva
                        var prevHourOccupied = _model.NewBoolVar($"prev_{schoolClass.Name}_d{d}_h{h}");
                        var currHourOccupied = _model.NewBoolVar($"curr_{schoolClass.Name}_d{d}_h{h}");
                        var nextHourOccupied = _model.NewBoolVar($"next_{schoolClass.Name}_d{d}_h{h}");

                        var prevActivities = new List<OrTools.IntVar>();
                        var currActivities = new List<OrTools.IntVar>();
                        var nextActivities = new List<OrTools.IntVar>();

                        foreach (var activity in classActivities)
                        {
                            // Controlla quali attività occupano ogni ora
                            for (int startHour = 1; startHour <= maxHours - activity.WeeklyHours + 1; startHour++)
                            {
                                string varName = $"act_{activity.Id}_d{d}_start{startHour}";
                                if (!_variables.ContainsKey(varName)) continue;

                                // Ora precedente (h-1)
                                if (h - 1 >= startHour && h - 1 < startHour + activity.WeeklyHours)
                                    prevActivities.Add(_variables[varName]);

                                // Ora corrente (h)
                                if (h >= startHour && h < startHour + activity.WeeklyHours)
                                    currActivities.Add(_variables[varName]);

                                // Ora successiva (h+1)
                                if (h + 1 >= startHour && h + 1 < startHour + activity.WeeklyHours)
                                    nextActivities.Add(_variables[varName]);
                            }
                        }

                        if (prevActivities.Count > 0 && currActivities.Count > 0 && nextActivities.Count > 0)
                        {
                            _model.Add(OrTools.LinearExpr.Sum(prevActivities) > 0).OnlyEnforceIf(prevHourOccupied);
                            _model.Add(OrTools.LinearExpr.Sum(prevActivities) == 0).OnlyEnforceIf(prevHourOccupied.Not());

                            _model.Add(OrTools.LinearExpr.Sum(currActivities) > 0).OnlyEnforceIf(currHourOccupied);
                            _model.Add(OrTools.LinearExpr.Sum(currActivities) == 0).OnlyEnforceIf(currHourOccupied.Not());

                            _model.Add(OrTools.LinearExpr.Sum(nextActivities) > 0).OnlyEnforceIf(nextHourOccupied);
                            _model.Add(OrTools.LinearExpr.Sum(nextActivities) == 0).OnlyEnforceIf(nextHourOccupied.Not());

                            // Se c'è lezione prima E dopo, DEVE esserci anche in mezzo (no buchi)
                            var hasBothEnds = _model.NewBoolVar($"ends_{schoolClass.Name}_d{d}_h{h}");
                            _model.AddBoolAnd(new[] { prevHourOccupied, nextHourOccupied }).OnlyEnforceIf(hasBothEnds);

                            // Se hasBothEnds allora currHourOccupied
                            _model.AddImplication(hasBothEnds, currHourOccupied);
                        }
                    }
                }
            }

            // 9. NUOVO: Vincolo MIN ore giornaliere per TUTTI i docenti - CON DEBUG
            ReportProgress(44, "Aggiunta vincolo min ore giornaliere docenti...");

            int minDailyHoursForTeachers = 2;
            var debugInfo = new System.Text.StringBuilder();
            debugInfo.AppendLine("=== DEBUG VINCOLO MIN ORE DOCENTI ===");
            //.Take(3)
            foreach (var teacher in _teachers) // DEBUG: Solo primi 3 docenti per non avere troppo output
            {
                var teacherActivities = _activities.Where(a => a.TeacherFullName == teacher.FullName).ToList();

                // === INIZIO ESCLUSIONE TEMPORANEA ===
                // TODO: Rimuovere quando Lepore avrà più ore
                if (teacher.FullName == "Lepore Massimo" || teacher.FullName == "Pignataro Concetta")
                {
                    ReportProgress(44, $"  Escluso temporaneamente {teacher.FullName} dal vincolo min ore");
                    continue;
                }


                // === DEBUG PIGNATARO ===
                if (teacher.FullName == "Pignataro Concetta")
                {
                    var debugPignataro = new System.Text.StringBuilder();
                    debugPignataro.AppendLine("=== DEBUG SPECIFICO PIGNATARO ===");
                    debugPignataro.AppendLine($"Attività totali: {teacherActivities.Count}");

                    // Analizza per ogni giorno
                    for (int d = 0; d < days.Count; d++)
                    {
                        debugPignataro.AppendLine($"\nGiorno {d} ({days[d]}):");

                        var hourSums = new List<OrTools.LinearExpr>();
                        var possibleAssignments = new List<string>();

                        foreach (var activity in teacherActivities)
                        {
                            for (int startHour = 1; startHour <= maxHours - activity.WeeklyHours + 1; startHour++)
                            {
                                string varName = $"act_{activity.Id}_d{d}_start{startHour}";
                                if (_variables.ContainsKey(varName))
                                {
                                    hourSums.Add(_variables[varName] * activity.WeeklyHours);
                                    possibleAssignments.Add($"  - {activity.ClassName} ora {startHour}");
                                }
                            }
                        }

                        debugPignataro.AppendLine($"  Possibili assegnazioni: {hourSums.Count}");
                        debugPignataro.AppendLine($"  Classi che potrebbero andare in questo giorno:");
                        foreach (var pa in possibleAssignments.Take(5))
                        {
                            debugPignataro.AppendLine(pa);
                        }
                        if (possibleAssignments.Count > 5)
                            debugPignataro.AppendLine($"  ... e altre {possibleAssignments.Count - 5}");

                        // Verifica se il vincolo viene applicato
                        if (hourSums.Count > 0)
                        {
                            debugPignataro.AppendLine($"  -> Applico vincolo: se lavora, minimo {minDailyHoursForTeachers} ore");
                        }
                    }

                    // Verifica conflitti con vincolo 4
                    var groupedByClass = teacherActivities.GroupBy(a => a.ClassName);
                    debugPignataro.AppendLine($"\nAnalisi vincolo 4:");
                    foreach (var grp in groupedByClass)
                    {
                        if (grp.Count() > 1)
                        {
                            debugPignataro.AppendLine($"  ATTENZIONE: {grp.Count()} attività nella classe {grp.Key}");
                        }
                    }
                    debugPignataro.AppendLine($"Totale classi diverse: {groupedByClass.Count()}");

                    System.Windows.MessageBox.Show(debugPignataro.ToString(), "Debug Pignataro",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                // === FINE DEBUG PIGNATARO ===




                debugInfo.AppendLine($"\nDocente: {teacher.FullName}");
                debugInfo.AppendLine($"  Attività totali: {teacherActivities.Count}");
                debugInfo.AppendLine($"  Ore totali: {teacherActivities.Sum(a => a.WeeklyHours)}");

                // Mostra dettaglio attività
                var actByClass = teacherActivities.GroupBy(a => a.ClassName);
                foreach (var grp in actByClass)
                {
                    debugInfo.AppendLine($"  Classe {grp.Key}: {string.Join("+", grp.Select(a => a.WeeklyHours))} = {grp.Sum(a => a.WeeklyHours)} ore");
                }

                for (int d = 0; d < days.Count; d++)
                {
                    var hourSums = new List<OrTools.LinearExpr>();
                    var dayActivities = new List<string>();

                    foreach (var activity in teacherActivities)
                    {
                        for (int startHour = 1; startHour <= maxHours - activity.WeeklyHours + 1; startHour++)
                        {
                            string varName = $"act_{activity.Id}_d{d}_start{startHour}";
                            if (_variables.ContainsKey(varName))
                            {
                                hourSums.Add(_variables[varName] * activity.WeeklyHours);
                                dayActivities.Add($"Act{activity.Id}({activity.WeeklyHours}h in {activity.ClassName})");
                            }
                        }
                    }

                    debugInfo.AppendLine($"  Giorno {d}: {hourSums.Count} possibili assegnazioni, {dayActivities.Count} attività possibili");

                    if (hourSums.Count > 0)
                    {
                        var hoursInDay = _model.NewIntVar(0, maxHours, $"teacherHours_{teacher.FullName}_d{d}");
                        _model.Add(hoursInDay == OrTools.LinearExpr.Sum(hourSums));

                        var isWorking = _model.NewBoolVar($"working_{teacher.FullName}_d{d}");

                        _model.Add(hoursInDay > 0).OnlyEnforceIf(isWorking);
                        _model.Add(hoursInDay == 0).OnlyEnforceIf(isWorking.Not());

                        // VINCOLO CHIAVE: Se lavora, minimo 2 ore
                        _model.Add(hoursInDay >= minDailyHoursForTeachers).OnlyEnforceIf(isWorking);
                        _model.Add(hoursInDay <= 5);

                        debugInfo.AppendLine($"    -> Vincolo: se lavora, tra {minDailyHoursForTeachers} e 5 ore");
                    }
                }
            }

            //Debug.Print(debugInfo.ToString());
            // Mostra il debug
            //System.Windows.MessageBox.Show(debugInfo.ToString(), "Debug Vincolo Min Ore",
            //    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

            ReportProgress(45, "Vincolo min ore docenti aggiunto");

            ReportProgress(46, "Vincoli base aggiunti con successo");
        }


        public void AnalyzeMinHoursInfeasibility()
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine("=== ANALISI INFEASIBILITY VINCOLO MIN ORE ===\n");

            var days = _config.GetActiveDays();
            int minHours = 2;
            var problematicTeachers = new List<string>();

            foreach (var teacher in _teachers)
            {
                var teacherActivities = _activities.Where(a => a.TeacherFullName == teacher.FullName).ToList();
                var totalHours = teacherActivities.Sum(a => a.WeeklyHours);

                // Analizza distribuzione per vincolo 4
                var groupsByClassSubject = teacherActivities
                    .GroupBy(a => new { a.ClassName, a.Subject })
                    .ToList();

                // Calcola il numero minimo di giorni necessari per rispettare vincolo 4
                int minDaysNeededForConstraint4 = groupsByClassSubject.Sum(g => g.Count());

                // Calcola il numero massimo di giorni lavorativi con min 2 ore
                int maxWorkDaysWithMin2 = totalHours / minHours; // floor division

                // Se serve lavorare più giorni di quelli possibili con min 2 ore
                if (minDaysNeededForConstraint4 > days.Count ||
                    (minDaysNeededForConstraint4 > maxWorkDaysWithMin2))
                {
                    problematicTeachers.Add(teacher.FullName);

                    report.AppendLine($"DOCENTE PROBLEMATICO: {teacher.FullName}");
                    report.AppendLine($"  Ore totali: {totalHours}");
                    report.AppendLine($"  Attività totali: {teacherActivities.Count}");
                    report.AppendLine($"  Gruppi classe/materia: {groupsByClassSubject.Count}");

                    // Dettaglio per gruppo
                    foreach (var group in groupsByClassSubject.Take(5)) // primi 5 per brevità
                    {
                        var activities = group.ToList();
                        report.AppendLine($"  - {group.Key.ClassName}/{group.Key.Subject}: {string.Join("+", activities.Select(a => a.WeeklyHours))} = {activities.Sum(a => a.WeeklyHours)} ore in {activities.Count} attività");
                    }

                    report.AppendLine($"  Giorni minimi per vincolo 4: {minDaysNeededForConstraint4}");
                    report.AppendLine($"  Giorni max con min 2 ore: {maxWorkDaysWithMin2}");

                    // Simula distribuzione ottimale
                    report.AppendLine($"  Distribuzione teorica migliore:");

                    // Prova a distribuire
                    var distribution = SimulateDistribution(teacherActivities, groupsByClassSubject, days.Count, minHours);
                    if (distribution != null)
                    {
                        for (int d = 0; d < days.Count && d < distribution.Length; d++)
                        {
                            if (distribution[d] > 0)
                                report.AppendLine($"    Giorno {d + 1}: {distribution[d]} ore");
                        }
                    }
                    else
                    {
                        report.AppendLine($"    IMPOSSIBILE rispettare tutti i vincoli!");
                    }

                    report.AppendLine();
                }
            }

            report.AppendLine($"\nTOTALE DOCENTI PROBLEMATICI: {problematicTeachers.Count}");
            foreach (var t in problematicTeachers)
            {
                report.AppendLine($"  - {t}");
            }

            System.Windows.MessageBox.Show(report.ToString(), "Analisi Infeasibility Min Ore",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        private int[] SimulateDistribution<TKey>(List<Activity> activities,
            List<IGrouping<TKey, Activity>> groups, int days, int minHours)
        {
            // Simula una distribuzione che rispetti sia vincolo 4 che vincolo min ore
            var distribution = new int[days];
            var dayIndex = 0;

            foreach (var group in groups)
            {
                foreach (var activity in group)
                {
                    // Trova un giorno dove mettere questa attività
                    bool placed = false;
                    for (int d = 0; d < days && !placed; d++)
                    {
                        int actualDay = (dayIndex + d) % days;

                        // Controlla se possiamo aggiungere qui
                        if (distribution[actualDay] + activity.WeeklyHours <= 5) // max 5 ore
                        {
                            distribution[actualDay] += activity.WeeklyHours;
                            placed = true;
                            dayIndex = (actualDay + 1) % days;
                        }
                    }

                    if (!placed) return null; // Impossibile distribuire
                }
            }

            // Verifica che tutti i giorni con ore abbiano almeno minHours
            for (int d = 0; d < days; d++)
            {
                if (distribution[d] > 0 && distribution[d] < minHours)
                {
                    return null; // Viola vincolo min ore
                }
            }

            return distribution;
        }

        public void DiagnoseInfeasibility()
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine("=== DIAGNOSI INFEASIBILITY ===");

            // Verifica 1: Blocchi troppo grandi
            var largeBlocks = _activities.Where(a => a.WeeklyHours > 3).ToList();
            if (largeBlocks.Any())
            {
                report.AppendLine($"\n⚠ Attività con blocchi grandi:");
                foreach (var act in largeBlocks.Take(10))
                {
                    report.AppendLine($"  - {act.TeacherFullName} in {act.ClassName}: {act.WeeklyHours} ore consecutive");
                }
            }

            // Verifica 2: Docenti sovraccarichi
            var teacherLoads = _activities.GroupBy(a => a.TeacherFullName)
                .Select(g => new { Teacher = g.Key, TotalHours = g.Sum(a => a.WeeklyHours), Activities = g.Count() })
                .OrderByDescending(t => t.TotalHours)
                .Take(10);

            report.AppendLine($"\n📊 Carico docenti (top 10):");
            foreach (var t in teacherLoads)
            {
                report.AppendLine($"  - {t.Teacher}: {t.TotalHours} ore totali, {t.Activities} attività");
            }

            // Verifica 3: Conflitti stesso docente/classe/materia
            var conflicts = _activities
                .GroupBy(a => new { a.TeacherFullName, a.ClassName, a.Subject })
                .Where(g => g.Count() > 1)
                .ToList();

            report.AppendLine($"\n🔄 Attività da distribuire su giorni diversi: {conflicts.Count} gruppi");

            // Verifica 4: Fattibilità matematica per classi
            var days = _config.GetActiveDays();
            report.AppendLine($"\n📐 Analisi fattibilità classi (con min={_config.MinDailyHours}, max={_config.MaxDailyHours}):");

            foreach (var cls in _classes.Take(5))
            {
                var totalHours = _activities.Where(a => a.ClassName == cls.Name).Sum(a => a.WeeklyHours);
                var minPossible = days.Count * _config.MinDailyHours;
                var maxPossible = days.Count * _config.MaxDailyHours;

                if (totalHours < minPossible || totalHours > maxPossible)
                {
                    report.AppendLine($"  ❌ {cls.Name}: {totalHours} ore (min possibile: {minPossible}, max: {maxPossible})");
                }
            }

            System.Windows.MessageBox.Show(report.ToString(), "Diagnosi",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        private void AddBaseConstraintsOLD()
        {
            var days = _config.GetActiveDays();
            var maxHours = _config.MaxDailyHours;

            ReportProgress(25, $"Configurazione: {days.Count} giorni, max {maxHours} ore/giorno");

            // DEBUG: Verifica dati
            int totalHoursNeeded = _activities.Sum(a => a.WeeklyHours);
            int totalSlotsAvailable = days.Count * maxHours * _classes.Count;
            ReportProgress(30, $"Ore necessarie: {totalHoursNeeded}, Slot disponibili: {totalSlotsAvailable}");

            if (totalHoursNeeded > totalSlotsAvailable)
            {
                ReportProgress(30, $"ATTENZIONE: Più ore richieste che slot disponibili!", true);
            }

            // 1. Ogni attività deve essere assegnata esattamente per il numero di ore settimanali
            foreach (var activity in _activities)
            {
                var activityVars = new List<OrTools.IntVar>();

                for (int d = 0; d < days.Count; d++)
                {
                    for (int h = 1; h <= maxHours; h++)
                    {
                        string varName = $"act_{activity.Id}_d{d}_h{h}";
                        if (_variables.ContainsKey(varName))
                        {
                            activityVars.Add(_variables[varName]);
                        }
                    }
                }

                if (activityVars.Count > 0)
                {
                    // La somma deve essere uguale alle ore settimanali dell'attività
                    _model.Add(OrTools.LinearExpr.Sum(activityVars) == activity.WeeklyHours);
                }
                else
                {
                    ReportProgress(35, $"ERRORE: Nessuna variabile per attività {activity.Id}", true);
                }
            }

            // 2. Un docente può essere in un solo posto alla volta
            foreach (var teacher in _teachers)
            {
                var teacherActivities = _activities.Where(a => a.TeacherFullName == teacher.FullName).ToList();

                for (int d = 0; d < days.Count; d++)
                {
                    for (int h = 1; h <= maxHours; h++)
                    {
                        var slotVars = new List<OrTools.IntVar>();

                        foreach (var activity in teacherActivities)
                        {
                            string varName = $"act_{activity.Id}_d{d}_h{h}";
                            if (_variables.ContainsKey(varName))
                            {
                                slotVars.Add(_variables[varName]);
                            }
                        }

                        // Al massimo una attività per docente per slot
                        if (slotVars.Count > 1)  // Solo se ci sono più attività
                        {
                            _model.Add(OrTools.LinearExpr.Sum(slotVars) <= 1);
                        }
                    }
                }
            }

            // 3. Una classe può avere una sola lezione alla volta (NON articolate)
            foreach (var schoolClass in _classes)
            {
                // Separa attività normali e articolate
                var normalActivities = _activities
                    .Where(a => a.ClassName == schoolClass.Name && string.IsNullOrWhiteSpace(a.ArticulationGroup))
                    .ToList();

                for (int d = 0; d < days.Count; d++)
                {
                    for (int h = 1; h <= maxHours; h++)
                    {
                        var slotVars = new List<OrTools.IntVar>();

                        foreach (var activity in normalActivities)
                        {
                            string varName = $"act_{activity.Id}_d{d}_h{h}";
                            if (_variables.ContainsKey(varName))
                            {
                                slotVars.Add(_variables[varName]);
                            }
                        }

                        // Al massimo una attività normale per classe per slot
                        if (slotVars.Count > 1)
                        {
                            _model.Add(OrTools.LinearExpr.Sum(slotVars) <= 1);
                        }
                    }
                }
            }

            // 4. Gestione attività articolate - VERSIONE SEMPLIFICATA
            var articulatedByClassAndGroup = _activities
                .Where(a => !string.IsNullOrWhiteSpace(a.ArticulationGroup))
                .GroupBy(a => new { a.ClassName, a.ArticulationGroup })
                .ToList();

            ReportProgress(38, $"Trovati {articulatedByClassAndGroup.Count} gruppi articolati");

            foreach (var group in articulatedByClassAndGroup)
            {
                var groupActivities = group.ToList();
                if (groupActivities.Count < 2) continue;

                // Per ora, assicuriamoci solo che abbiano lo stesso numero di ore
                // (vincolo più rilassato per evitare infeasible)
                var firstActivity = groupActivities[0];
                for (int i = 1; i < groupActivities.Count; i++)
                {
                    if (groupActivities[i].WeeklyHours != firstActivity.WeeklyHours)
                    {
                        ReportProgress(38, $"ATTENZIONE: Attività articolate con ore diverse nel gruppo {group.Key}", true);
                    }
                }

                // Vincolo semplificato: devono avvenire in parallelo
                // ma solo per alcuni slot, non necessariamente tutti uguali
                // Questo è più rilassato e evita infeasible
            }

            // SOSTITUISCI il punto 5 del metodo AddBaseConstraints con questa versione corretta:

            // 5. Vincolo sulle ore giornaliere delle classi CON MINIMO CORRETTO
            foreach (var schoolClass in _classes)
            {
                var classActivities = _activities.Where(a => a.ClassName == schoolClass.Name).ToList();
                int totalClassHours = classActivities.Sum(a => a.WeeklyHours);

                // Configurazione min/max dalla config generale
                int minDailyForClass = _config.MinDailyHours;  // Dalla configurazione (es. 5)
                int maxDailyForClass = _config.MaxDailyHours;  // Dalla configurazione (es. 6)

                for (int d = 0; d < days.Count; d++)
                {
                    var dayVars = new List<OrTools.IntVar>();

                    foreach (var activity in classActivities)
                    {
                        for (int h = 1; h <= maxHours; h++)
                        {
                            string varName = $"act_{activity.Id}_d{d}_h{h}";
                            if (_variables.ContainsKey(varName))
                            {
                                dayVars.Add(_variables[varName]);
                            }
                        }
                    }

                    if (dayVars.Count > 0)
                    {
                        var dayTotal = _model.NewIntVar(0, maxDailyForClass, $"dayTotal_{schoolClass.Name}_d{d}");
                        _model.Add(dayTotal == OrTools.LinearExpr.Sum(dayVars));

                        // IMPORTANTE: La classe o fa 0 ore (non viene) o fa ALMENO minDailyForClass
                        // Questo è il vincolo che mancava!

                        // Creiamo una variabile booleana per indicare se la classe ha scuola quel giorno
                        var hasSchool = _model.NewBoolVar($"hasSchool_{schoolClass.Name}_d{d}");

                        // Se dayTotal > 0, allora hasSchool = true
                        _model.Add(dayTotal > 0).OnlyEnforceIf(hasSchool);
                        // Se hasSchool = false, allora dayTotal = 0
                        _model.Add(dayTotal == 0).OnlyEnforceIf(hasSchool.Not());

                        // VINCOLO CHIAVE: Se hasSchool = true, allora dayTotal >= minDailyForClass
                        _model.Add(dayTotal >= minDailyForClass).OnlyEnforceIf(hasSchool);
                        // E ovviamente dayTotal <= maxDailyForClass
                        _model.Add(dayTotal <= maxDailyForClass);
                    }
                }

                // VINCOLO AGGIUNTIVO: Assicuriamoci che la classe faccia esattamente le sue ore totali
                // Questo era già presente ma lo ribadiamo per chiarezza
                var allClassVars = new List<OrTools.IntVar>();
                for (int d = 0; d < days.Count; d++)
                {
                    foreach (var activity in classActivities)
                    {
                        for (int h = 1; h <= maxHours; h++)
                        {
                            string varName = $"act_{activity.Id}_d{d}_h{h}";
                            if (_variables.ContainsKey(varName))
                            {
                                allClassVars.Add(_variables[varName]);
                            }
                        }
                    }
                }

                // La somma totale deve essere esattamente le ore della classe
                if (allClassVars.Count > 0)
                {
                    _model.Add(OrTools.LinearExpr.Sum(allClassVars) == totalClassHours);
                }
            }

            // 6. NUOVO: Vincolo di consecutività per le classi (NO BUCHI)
            ReportProgress(39, "Aggiunta vincoli consecutività classi...");

            foreach (var schoolClass in _classes)
            {
                var classActivities = _activities.Where(a => a.ClassName == schoolClass.Name).ToList();

                for (int d = 0; d < days.Count; d++)
                {
                    // Per ogni giorno, se la classe ha lezioni, devono essere consecutive

                    // Crea variabili per tracciare prima e ultima ora del giorno
                    var firstHour = _model.NewIntVar(0, maxHours, $"first_{schoolClass.Name}_d{d}");
                    var lastHour = _model.NewIntVar(0, maxHours, $"last_{schoolClass.Name}_d{d}");

                    // Variabile per contare le ore totali del giorno
                    var dayHours = new List<OrTools.IntVar>();

                    for (int h = 1; h <= maxHours; h++)
                    {
                        var hourVars = new List<OrTools.IntVar>();

                        foreach (var activity in classActivities)
                        {
                            string varName = $"act_{activity.Id}_d{d}_h{h}";
                            if (_variables.ContainsKey(varName))
                            {
                                hourVars.Add(_variables[varName]);
                            }
                        }

                        if (hourVars.Count > 0)
                        {
                            // Variabile che indica se c'è lezione all'ora h
                            var hasLesson = _model.NewBoolVar($"has_{schoolClass.Name}_d{d}_h{h}");
                            _model.Add(OrTools.LinearExpr.Sum(hourVars) == 1).OnlyEnforceIf(hasLesson);
                            _model.Add(OrTools.LinearExpr.Sum(hourVars) == 0).OnlyEnforceIf(hasLesson.Not());

                            dayHours.Add(hasLesson);

                            // Se c'è lezione all'ora h, aggiorna prima e ultima ora
                            _model.Add(firstHour <= h).OnlyEnforceIf(hasLesson);
                            _model.Add(lastHour >= h).OnlyEnforceIf(hasLesson);
                        }
                    }

                    if (dayHours.Count > 0)
                    {
                        // Il numero di ore tra la prima e l'ultima deve essere uguale al totale delle ore
                        // Questo garantisce che non ci siano buchi
                        var totalDayHours = _model.NewIntVar(0, maxHours, $"total_{schoolClass.Name}_d{d}");
                        _model.Add(totalDayHours == OrTools.LinearExpr.Sum(dayHours));

                        // Formula: se totalDayHours > 0, allora (lastHour - firstHour + 1) == totalDayHours
                        // Questo assicura che tutte le ore siano consecutive
                        var isDayActive = _model.NewBoolVar($"active_{schoolClass.Name}_d{d}");
                        _model.Add(totalDayHours > 0).OnlyEnforceIf(isDayActive);
                        _model.Add(totalDayHours == 0).OnlyEnforceIf(isDayActive.Not());

                        // Se il giorno è attivo, le ore devono essere consecutive
                        var span = _model.NewIntVar(0, maxHours, $"span_{schoolClass.Name}_d{d}");
                        _model.Add(span == lastHour - firstHour + 1).OnlyEnforceIf(isDayActive);
                        _model.Add(span == totalDayHours).OnlyEnforceIf(isDayActive);
                    }
                }
            }

            ReportProgress(40, "Vincoli consecutività aggiunti");

            // 7. NUOVO: Vincolo max ore giornaliere per TUTTI i docenti
            ReportProgress(41, "Aggiunta vincolo max ore giornaliere docenti...");

            int maxDailyHoursForTeachers = 5; // Configurabile in futuro

            foreach (var teacher in _teachers)
            {
                var teacherActivities = _activities.Where(a => a.TeacherFullName == teacher.FullName).ToList();

                for (int d = 0; d < days.Count; d++)
                {
                    var dayVars = new List<OrTools.IntVar>();

                    foreach (var activity in teacherActivities)
                    {
                        for (int h = 1; h <= maxHours; h++)
                        {
                            string varName = $"act_{activity.Id}_d{d}_h{h}";
                            if (_variables.ContainsKey(varName))
                            {
                                dayVars.Add(_variables[varName]);
                            }
                        }
                    }

                    if (dayVars.Count > 0)
                    {
                        // Max 5 ore al giorno per ogni docente
                        _model.Add(OrTools.LinearExpr.Sum(dayVars) <= maxDailyHoursForTeachers);
                    }
                }
            }

            // 8. NUOVO: Le classi devono iniziare dalla prima ora (no entrate posticipate)
            ReportProgress(42, "Aggiunta vincolo ingresso prima ora...");

            foreach (var schoolClass in _classes)
            {
                var classActivities = _activities.Where(a => a.ClassName == schoolClass.Name).ToList();

                for (int d = 0; d < days.Count; d++)
                {
                    // Se la classe ha lezioni in un giorno, DEVE avere la prima ora
                    var firstHourVars = new List<OrTools.IntVar>();
                    var anyHourVars = new List<OrTools.IntVar>();

                    foreach (var activity in classActivities)
                    {
                        // Variabili per la prima ora
                        string firstHourVar = $"act_{activity.Id}_d{d}_h1";
                        if (_variables.ContainsKey(firstHourVar))
                        {
                            firstHourVars.Add(_variables[firstHourVar]);
                        }

                        // Variabili per qualsiasi ora del giorno
                        for (int h = 1; h <= maxHours; h++)
                        {
                            string varName = $"act_{activity.Id}_d{d}_h{h}";
                            if (_variables.ContainsKey(varName))
                            {
                                anyHourVars.Add(_variables[varName]);
                            }
                        }
                    }

                    if (firstHourVars.Count > 0 && anyHourVars.Count > 0)
                    {
                        // Se c'è almeno una lezione nel giorno, deve esserci una lezione alla prima ora
                        var hasAnyLesson = _model.NewBoolVar($"hasAny_{schoolClass.Name}_d{d}");
                        var hasFirstHour = _model.NewBoolVar($"hasFirst_{schoolClass.Name}_d{d}");

                        _model.Add(OrTools.LinearExpr.Sum(anyHourVars) > 0).OnlyEnforceIf(hasAnyLesson);
                        _model.Add(OrTools.LinearExpr.Sum(anyHourVars) == 0).OnlyEnforceIf(hasAnyLesson.Not());

                        _model.Add(OrTools.LinearExpr.Sum(firstHourVars) > 0).OnlyEnforceIf(hasFirstHour);
                        _model.Add(OrTools.LinearExpr.Sum(firstHourVars) == 0).OnlyEnforceIf(hasFirstHour.Not());

                        // Se hasAnyLesson allora hasFirstHour
                        _model.AddImplication(hasAnyLesson, hasFirstHour);
                    }
                }
            }
        }

        // Aggiungi questo metodo di debug
        public void DebugDataConsistency()
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine("=== ANALISI DATI ===");

            // Verifica ore totali
            var totalHours = _activities.Sum(a => a.WeeklyHours);
            report.AppendLine($"Ore totali richieste: {totalHours}");

            // Verifica per classe
            foreach (var cls in _classes)
            {
                var classHours = _activities.Where(a => a.ClassName == cls.Name).Sum(a => a.WeeklyHours);
                report.AppendLine($"Classe {cls.Name}: {classHours} ore, {cls.TotalWeeklyHours} dichiarate");

                if (classHours != cls.TotalWeeklyHours)
                {
                    report.AppendLine($"  ⚠ ATTENZIONE: Discrepanza ore!");
                }
            }

            // Verifica per docente
            foreach (var teacher in _teachers.Take(5)) // Solo i primi 5 per brevità
            {
                var teacherHours = _activities.Where(a => a.TeacherFullName == teacher.FullName).Sum(a => a.WeeklyHours);
                report.AppendLine($"Docente {teacher.FullName}: {teacherHours} ore");
            }

            // Verifica slot disponibili
            var days = _config.GetActiveDays();
            var slotsPerClass = days.Count * _config.MaxDailyHours;
            report.AppendLine($"\nSlot disponibili per classe: {slotsPerClass}");

            // Classi con troppi ore
            var problematicClasses = _classes.Where(c => c.TotalWeeklyHours > slotsPerClass).ToList();
            if (problematicClasses.Any())
            {
                report.AppendLine("\n⚠ CLASSI CON TROPPE ORE:");
                foreach (var cls in problematicClasses)
                {
                    report.AppendLine($"  {cls.Name}: {cls.TotalWeeklyHours} ore su {slotsPerClass} slot");
                }
            }

            System.Windows.MessageBox.Show(report.ToString(), "Debug Dati",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        private void AddUserConstraints()
        {
            ReportProgress(65, $"Applicazione di {_constraints.Count} vincoli utente...");

            foreach (var constraint in _constraints.Where(c => c.IsActive))
            {
                try
                {
                    switch (constraint)
                    {
                        case TeacherMaxWeeklyGapsConstraint gapConstraint:
                            ApplyTeacherGapsConstraint(gapConstraint);
                            break;

                        case TeacherMaxDailyHoursConstraint dailyConstraint:
                            ApplyTeacherDailyHoursConstraint(dailyConstraint);
                            break;

                        case TeacherAvailabilityConstraint availConstraint:
                            ApplyTeacherAvailabilityConstraint(availConstraint);
                            break;

                        case TeacherDayOffConstraint dayOffConstraint:
                            ApplyTeacherDayOffConstraint(dayOffConstraint);
                            break;

                        case ClassDailyHoursConstraint classHoursConstraint:
                            ApplyClassDailyHoursConstraint(classHoursConstraint);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    ReportProgress(65, $"Errore applicando vincolo {constraint.Name}: {ex.Message}");
                }
            }
        }

        // NUOVO: Vincolo ore buche docente adattato ai blocchi
        private void ApplyTeacherGapsConstraint(TeacherMaxWeeklyGapsConstraint constraint)
        {
            var teacherActivities = _activities
                .Where(a => a.TeacherFullName == constraint.TeacherName)
                .ToList();

            if (teacherActivities.Count == 0) return;

            var days = _config.GetActiveDays();
            var maxHours = _config.MaxDailyHours;

            // Conta le ore buche per ogni giorno
            var totalGaps = _model.NewIntVar(0, days.Count * maxHours, $"totalGaps_{constraint.TeacherName}");
            var dayGaps = new List<OrTools.IntVar>();

            for (int d = 0; d < days.Count; d++)
            {
                // Per ogni giorno, calcola prima e ultima ora
                var firstHour = _model.NewIntVar(0, maxHours + 1, $"first_{constraint.TeacherName}_d{d}");
                var lastHour = _model.NewIntVar(0, maxHours + 1, $"last_{constraint.TeacherName}_d{d}");
                var totalHours = _model.NewIntVar(0, maxHours, $"total_{constraint.TeacherName}_d{d}");

                // Conta le ore occupate
                var hoursOccupied = new OrTools.BoolVar[maxHours + 1];
                for (int h = 1; h <= maxHours; h++)
                {
                    hoursOccupied[h] = _model.NewBoolVar($"occupied_{constraint.TeacherName}_d{d}_h{h}");

                    var occupyingActivities = new List<OrTools.IntVar>();

                    foreach (var activity in teacherActivities)
                    {
                        // Controlla se l'attività occupa l'ora h
                        for (int startHour = Math.Max(1, h - activity.WeeklyHours + 1);
                             startHour <= Math.Min(h, maxHours - activity.WeeklyHours + 1);
                             startHour++)
                        {
                            string varName = $"act_{activity.Id}_d{d}_start{startHour}";
                            if (_variables.ContainsKey(varName))
                            {
                                if (h >= startHour && h < startHour + activity.WeeklyHours)
                                {
                                    occupyingActivities.Add(_variables[varName]);
                                }
                            }
                        }
                    }

                    if (occupyingActivities.Count > 0)
                    {
                        _model.Add(OrTools.LinearExpr.Sum(occupyingActivities) > 0).OnlyEnforceIf(hoursOccupied[h]);
                        _model.Add(OrTools.LinearExpr.Sum(occupyingActivities) == 0).OnlyEnforceIf(hoursOccupied[h].Not());

                        // Aggiorna prima e ultima ora
                        _model.Add(firstHour <= h).OnlyEnforceIf(hoursOccupied[h]);
                        _model.Add(lastHour >= h).OnlyEnforceIf(hoursOccupied[h]);
                    }
                }

                // Conta ore totali
                var occupiedList = hoursOccupied.Skip(1).Take(maxHours).ToList();
                _model.Add(totalHours == OrTools.LinearExpr.Sum(occupiedList));

                // Calcola buchi: (ultima - prima + 1) - totale
                var span = _model.NewIntVar(0, maxHours, $"span_{constraint.TeacherName}_d{d}");
                var gaps = _model.NewIntVar(0, maxHours, $"gaps_{constraint.TeacherName}_d{d}");

                // Se ci sono lezioni
                var hasLessons = _model.NewBoolVar($"hasLessons_{constraint.TeacherName}_d{d}");
                _model.Add(totalHours > 0).OnlyEnforceIf(hasLessons);
                _model.Add(totalHours == 0).OnlyEnforceIf(hasLessons.Not());

                // Calcola span e gaps solo se ci sono lezioni
                _model.Add(span == lastHour - firstHour + 1).OnlyEnforceIf(hasLessons);
                _model.Add(span == 0).OnlyEnforceIf(hasLessons.Not());

                _model.Add(gaps == span - totalHours).OnlyEnforceIf(hasLessons);
                _model.Add(gaps == 0).OnlyEnforceIf(hasLessons.Not());

                dayGaps.Add(gaps);
            }

            // Somma totale gaps
            _model.Add(totalGaps == OrTools.LinearExpr.Sum(dayGaps));

            // Applica il vincolo
            _model.Add(totalGaps <= constraint.MaxGaps);

            ReportProgress(66, $"Vincolo gaps applicato per {constraint.TeacherName}: max {constraint.MaxGaps}");
        }

        // Altri metodi per gli altri tipi di vincoli...
        private void ApplyTeacherDailyHoursConstraint(TeacherMaxDailyHoursConstraint constraint)
        {
            // Già implementato nel vincolo generale, ma qui puoi fare override specifici
        }

        private void ApplyTeacherAvailabilityConstraint(TeacherAvailabilityConstraint constraint)
        {
            var teacherActivities = _activities
                .Where(a => a.TeacherFullName == constraint.TeacherName)
                .ToList();

            var days = _config.GetActiveDays();

            foreach (var slot in constraint.UnavailableSlots)
            {
                int dayIndex = days.IndexOf(slot.Day);
                if (dayIndex < 0) continue;

                foreach (var activity in teacherActivities)
                {
                    string varName = $"act_{activity.Id}_d{dayIndex}_h{slot.Hour}";
                    if (_variables.ContainsKey(varName))
                    {
                        // Forza questa variabile a 0 (non disponibile)
                        _model.Add(_variables[varName] == 0);
                    }
                }
            }
        }

        private void ApplyTeacherDayOffConstraint(TeacherDayOffConstraint constraint)
        {
            var teacherActivities = _activities
                .Where(a => a.TeacherFullName == constraint.TeacherName)
                .ToList();

            var days = _config.GetActiveDays();
            int dayIndex = days.IndexOf(constraint.DayOff);

            if (dayIndex >= 0)
            {
                foreach (var activity in teacherActivities)
                {
                    for (int h = 1; h <= _config.MaxDailyHours; h++)
                    {
                        string varName = $"act_{activity.Id}_d{dayIndex}_h{h}";
                        if (_variables.ContainsKey(varName))
                        {
                            _model.Add(_variables[varName] == 0);
                        }
                    }
                }
            }
        }

        private void ApplyClassDailyHoursConstraint(ClassDailyHoursConstraint constraint)
        {
            // Implementazione per vincoli specifici delle classi
        }

        private void AddOptimizationObjective()
        {
            // Minimizza le ore buche dei docenti
            // Per ora implementazione semplificata
            // TODO: Implementare calcolo ore buche
        }

        private GeneratedSchedule ExtractSolution(OrTools.CpSolver solver)
        {
            var schedule = new GeneratedSchedule();
            var days = _config.GetActiveDays();
            var maxHours = _config.MaxDailyHours;

            foreach (var activity in _activities)
            {
                // Trova dove inizia questa attività
                for (int d = 0; d < days.Count; d++)
                {
                    for (int startHour = 1; startHour <= maxHours - activity.WeeklyHours + 1; startHour++)
                    {
                        string varName = $"act_{activity.Id}_d{d}_start{startHour}";

                        if (_variables.ContainsKey(varName) && solver.Value(_variables[varName]) == 1)
                        {
                            // L'attività inizia qui, crea slot per tutte le ore consecutive
                            for (int h = 0; h < activity.WeeklyHours; h++)
                            {
                                var slot = new ScheduleSlot
                                {
                                    Day = days[d],
                                    Hour = startHour + h,
                                    ClassName = activity.ClassName,
                                    TeacherName = activity.TeacherFullName,
                                    Subject = activity.Subject,
                                    ArticulationGroup = activity.ArticulationGroup
                                };

                                schedule.Slots.Add(slot);
                            }
                            break; // Trovata la posizione, esci dal loop
                        }
                    }
                }
            }

            return schedule;
        }

        private void ReportProgress(int percentage, string message, bool hasError = false)
        {
            ProgressChanged?.Invoke(this, new GenerationProgress
            {
                Percentage = percentage,
                Message = message,
                IsCompleted = percentage >= 100,
                HasErrors = hasError
            });
        }

        // Callback per soluzioni intermedie
        private class SolutionCallback : OrTools.CpSolverSolutionCallback
        {
            private readonly ScheduleGenerator _generator;
            private int _solutionCount = 0;

            public SolutionCallback(ScheduleGenerator generator)
            {
                _generator = generator;
            }

            public override void OnSolutionCallback()
            {
                _solutionCount++;
                _generator.ReportProgress(85, $"Soluzione #{_solutionCount} trovata...");
            }
        }
    }
}