using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using ClosedXML.Excel;
using SchoolScheduler.Common.Models;

namespace SchoolScheduler.Common.Models
{
    public class ExcelImportService
    {
        public class ImportResult
        {
            public bool Success { get; set; }
            public string Message { get; set; } = string.Empty;
            public List<Activity> Activities { get; set; } = new List<Activity>();
            public List<Teacher> Teachers { get; set; } = new List<Teacher>();
            public List<SchoolClass> Classes { get; set; } = new List<SchoolClass>();
            public List<string> Warnings { get; set; } = new List<string>();
        }

        public async Task<ImportResult> ImportFromExcelAsync(string filePath, string sheetName)
        {
            return await Task.Run(() => ImportFromExcel(filePath, sheetName));
        }

        public ImportResult ImportFromExcel(string filePath, string sheetName)
        {
            var result = new ImportResult();

            try
            {
                if (!File.Exists(filePath))
                {
                    result.Message = $"File non trovato: {filePath}";
                    return result;
                }

                using (var workbook = new XLWorkbook(filePath))
                {
                    // Trova il foglio
                    if (!workbook.Worksheets.TryGetWorksheet(sheetName, out var worksheet))
                    {
                        // Se non specificato o non trovato, prendi il primo
                        worksheet = workbook.Worksheets.FirstOrDefault();
                        if (worksheet == null)
                        {
                            result.Message = "Nessun foglio trovato nel file Excel";
                            return result;
                        }
                        result.Warnings.Add($"Foglio '{sheetName}' non trovato, uso '{worksheet.Name}'");
                    }

                    // Leggi le attività
                    var activities = ReadActivities(worksheet, result);

                    // Deriva docenti e classi
                    var teachers = DeriveTeachers(activities);
                    var classes = DeriveClasses(activities);

                    // Identifica classi articolate
                    IdentifyArticulations(activities, classes);

                    result.Activities = activities;
                    result.Teachers = teachers;
                    result.Classes = classes;
                    result.Success = true;
                    result.Message = $"Importati: {activities.Count} attività, {teachers.Count} docenti, {classes.Count} classi";
                }
            }
            catch (Exception ex)
            {
                result.Message = $"Errore durante l'importazione: {ex.Message}";
                result.Success = false;
            }

            return result;
        }

        private List<Activity> ReadActivities(IXLWorksheet worksheet, ImportResult result)
        {
            var activities = new List<Activity>();
            var firstRow = worksheet.FirstRowUsed();

            if (firstRow == null)
            {
                result.Warnings.Add("Il foglio è vuoto");
                return activities;
            }

            // Trova gli indici delle colonne
            var columnMap = new Dictionary<string, int>();
            var cellsInRow = firstRow.CellsUsed();

            foreach (var cell in cellsInRow)
            {
                var headerText = cell.Value.ToString().Trim();
                columnMap[headerText] = cell.Address.ColumnNumber;
            }

            // Verifica colonne richieste
            var requiredColumns = new[] { "Cognome", "Nome", "CdC", "Classe", "Ore", "Materia" };
            foreach (var col in requiredColumns)
            {
                if (!columnMap.ContainsKey(col))
                {
                    result.Warnings.Add($"Colonna mancante: {col}");
                }
            }

            // Verifica se c'è la colonna Gruppo (opzionale)
            bool hasGruppoColumn = columnMap.ContainsKey("Gruppo");
            if (!hasGruppoColumn)
            {
                result.Warnings.Add("Colonna 'Gruppo' non trovata - le articolazioni non saranno riconosciute");
            }

            // Leggi i dati
            var currentRow = firstRow.RowBelow();
            int activityId = 1;
            int rowNumber = 2;

            while (!currentRow.IsEmpty())
            {
                try
                {
                    // Leggi i valori
                    var cognome = GetCellValue(currentRow, columnMap, "Cognome");
                    var nome = GetCellValue(currentRow, columnMap, "Nome");
                    var cdc = GetCellValue(currentRow, columnMap, "CdC");
                    var classe = GetCellValue(currentRow, columnMap, "Classe");
                    var oreStr = GetCellValue(currentRow, columnMap, "Ore");
                    var materia = GetCellValue(currentRow, columnMap, "Materia");
                    var gruppo = hasGruppoColumn ? GetCellValue(currentRow, columnMap, "Gruppo") : "";

                    // Filtra righe da ignorare
                    if (string.IsNullOrWhiteSpace(classe) ||
                        materia.Equals("disponibilità", StringComparison.OrdinalIgnoreCase))
                    {
                        currentRow = currentRow.RowBelow();
                        rowNumber++;
                        continue;
                    }

                    // Parsing ore
                    if (!int.TryParse(oreStr, out int ore))
                    {
                        result.Warnings.Add($"Riga {rowNumber}: ore non valide '{oreStr}'");
                        ore = 0;
                    }

                    // Crea il nome completo
                    var fullName = $"{cognome} {nome}".Trim();

                    var activity = new Activity
                    {
                        Id = activityId++,
                        TeacherFullName = fullName,
                        TeacherSurname = cognome.Trim(),
                        TeacherName = nome.Trim(),
                        ClassCode = cdc.Trim(),
                        ClassName = classe.Trim().ToUpper(),
                        Subject = materia.Trim(),
                        WeeklyHours = ore,
                        ArticulationGroup = gruppo.Trim().ToUpper()  // Aggiungi il gruppo
                    };

                    activities.Add(activity);
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Errore riga {rowNumber}: {ex.Message}");
                }

                currentRow = currentRow.RowBelow();
                rowNumber++;
            }

            return activities;
        }

        private string GetCellValue(IXLRow row, Dictionary<string, int> columnMap, string columnName)
        {
            if (columnMap.TryGetValue(columnName, out int colIndex))
            {
                var cell = row.Cell(colIndex);
                return cell.Value.ToString() ?? "";  // <-- Rimosso il ? dopo Value
            }
            return "";
        }

        private List<Teacher> DeriveTeachers(List<Activity> activities)
        {
            var teacherDict = new Dictionary<string, Teacher>();

            foreach (var activity in activities)
            {
                if (!teacherDict.TryGetValue(activity.TeacherFullName, out var teacher))
                {
                    teacher = new Teacher
                    {
                        Surname = activity.TeacherSurname,
                        Name = activity.TeacherName
                    };
                    teacherDict[activity.TeacherFullName] = teacher;
                }

                teacher.ClassCodes.Add(activity.ClassCode);
                teacher.TotalWeeklyHours += activity.WeeklyHours;
                teacher.Activities.Add(activity);
            }

            // Assegna ID
            int id = 1;
            foreach (var teacher in teacherDict.Values)
            {
                teacher.Id = id++;
            }

            return teacherDict.Values.OrderBy(t => t.FullName).ToList();
        }

        private List<SchoolClass> DeriveClasses(List<Activity> activities)
        {
            var classDict = new Dictionary<string, SchoolClass>();

            foreach (var activity in activities)
            {
                if (!classDict.TryGetValue(activity.ClassName, out var schoolClass))
                {
                    schoolClass = new SchoolClass
                    {
                        Name = activity.ClassName
                    };
                    classDict[activity.ClassName] = schoolClass;
                }

                schoolClass.TotalWeeklyHours += activity.WeeklyHours;
                schoolClass.Activities.Add(activity);
            }

            // Assegna ID
            int id = 1;
            foreach (var schoolClass in classDict.Values)
            {
                schoolClass.Id = id++;
            }

            return classDict.Values.OrderBy(c => c.Name).ToList();
        }

        private void IdentifyArticulations(List<Activity> activities, List<SchoolClass> classes)
        {
            foreach (var schoolClass in classes)
            {
                // Raggruppa le attività per gruppo di articolazione
                var articulatedGroups = schoolClass.Activities
                    .Where(a => !string.IsNullOrWhiteSpace(a.ArticulationGroup))
                    .GroupBy(a => a.ArticulationGroup)
                    .Where(g => g.Count() > 1)  // Almeno 2 attività nello stesso gruppo
                    .ToList();

                if (articulatedGroups.Any())
                {
                    schoolClass.HasArticulation = true;

                    // Crea i gruppi di articolazione
                    int groupId = 1;
                    foreach (var group in articulatedGroups)
                    {
                        var artGroup = new ArticulationGroup
                        {
                            Id = groupId++,
                            Name = group.Key,  // A1, A2, etc.
                            Activities = group.ToList()
                        };
                        schoolClass.ArticulationGroups.Add(artGroup);
                    }

                    // Rimossa la riga con result.Warnings.Add che causava l'errore
                }
                else
                {
                    schoolClass.HasArticulation = false;
                }
            }
        }
    }
}
