using System.Globalization;
using System.Text;

const int IterationsPerPair = 10_000;
const ulong BaseSeed = 20_261_118UL;

Strategy[] strategies = Enumerable.Range(0, 21)
    .Select(index => new Strategy(index * 5))
    .ToArray();

long[] totals = new long[strategies.Length];
int[] gamesPlayed = new int[strategies.Length];
int pairCount = 0;

// Each distinct unordered pair has its own deterministic pseudorandom stream.
for (int i = 0; i < strategies.Length; i++)
{
    for (int j = i + 1; j < strategies.Length; j++)
    {
        pairCount++;
        int firstPercent = strategies[i].BetrayalPercent;
        int secondPercent = strategies[j].BetrayalPercent;
        ulong pairSeed = BaseSeed ^ ((ulong)(firstPercent + 1) << 32) ^ (uint)(secondPercent + 1);
        var random = new SplitMix64(pairSeed);

        for (int iteration = 0; iteration < IterationsPerPair; iteration++)
        {
            bool firstBetrays = strategies[i].Betrays(ref random);
            bool secondBetrays = strategies[j].Betrays(ref random);
            (int firstPoints, int secondPoints) = Score(firstBetrays, secondBetrays);

            totals[i] += firstPoints;
            totals[j] += secondPoints;
            gamesPlayed[i]++;
            gamesPlayed[j]++;
        }
    }
}

var ranking = strategies
    .Select((strategy, index) => new Result(strategy.BetrayalPercent, totals[index], gamesPlayed[index]))
    .OrderByDescending(result => result.Points)
    .ThenByDescending(result => result.BetrayalPercent)
    .ToArray();

if (pairCount != 210 || ranking.Any(result => result.GamesPlayed != 200_000))
{
    throw new InvalidOperationException("Нарушено ожидаемое число пар или игр.");
}

string report = BuildReport(ranking, pairCount, IterationsPerPair, BaseSeed);
string projectDirectory = FindProjectDirectory();
string reportPath = Path.Combine(projectDirectory, "RESULTS.md");
File.WriteAllText(reportPath, report, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

Console.WriteLine($"Победитель: P{ranking[0].BetrayalPercent:D3} " +
                  $"({ranking[0].BetrayalPercent}% предательств), очков: {ranking[0].Points}");
Console.WriteLine($"Пар: {pairCount}, игр: {pairCount * IterationsPerPair:N0}");
Console.WriteLine($"Отчёт: {reportPath}");

static (int First, int Second) Score(bool firstBetrays, bool secondBetrays) =>
    (firstBetrays, secondBetrays) switch
    {
        (false, false) => (3, 3),
        (true, false) => (5, 0),
        (false, true) => (0, 5),
        (true, true) => (1, 1)
    };

static string FindProjectDirectory()
{
    for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
         directory is not null;
         directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Step001.Basic.csproj")))
        {
            return directory.FullName;
        }
    }

    throw new DirectoryNotFoundException("Не найдена папка Step001.Basic с файлом проекта.");
}

static string BuildReport(Result[] ranking, int pairs, int gamesPerPair, ulong seed)
{
    var report = new StringBuilder();
    Result winner = ranking[0];

    report.AppendLine("# Step001.Basic — результаты однократной дилеммы заключённого");
    report.AppendLine();
    report.AppendLine("## Условия эксперимента");
    report.AppendLine();
    report.AppendLine("- **Сценарий:** каждая игра состоит ровно из одного хода; у игроков нет памяти, информации о выборе соперника или адаптации.");
    report.AppendLine("- **Стратегии:** 21 вариант, вероятность предательства от 0% до 100% с шагом 5%.");
    report.AppendLine($"- **Турнир:** {pairs} различных неупорядоченных пар без самоигр, по {gamesPerPair.ToString("N0", CultureInfo.InvariantCulture)} независимых игр на пару; всего {(pairs * gamesPerPair).ToString("N0", CultureInfo.InvariantCulture)} игр.");
    report.AppendLine($"- **Игры на стратегию:** {ranking[0].GamesPlayed.ToString("N0", CultureInfo.InvariantCulture)}.");
    report.AppendLine("- **Выплаты:** C/C = 3/3, D/C = 5/0, C/D = 0/5, D/D = 1/1.");
    report.AppendLine($"- **Генератор:** SplitMix64, начальное зерно `{seed}`, отдельный поток на каждую пару.");
    report.AppendLine("- **Критерий победы:** максимальная сумма собственных очков за все игры.");
    report.AppendLine();
    report.AppendLine("## Итоговый рейтинг");
    report.AppendLine();
    report.AppendLine("| Место | Стратегия | Вероятность предательства | Всего очков | Средний балл за игру | Ожидаемые очки |");
    report.AppendLine("| ---: | :--- | ---: | ---: | ---: | ---: |");

    for (int index = 0; index < ranking.Length; index++)
    {
        Result result = ranking[index];
        double expectation = ExpectedTotal(result.BetrayalPercent, gamesPerPair);
        report.AppendLine($"| {index + 1} | P{result.BetrayalPercent:D3} | {result.BetrayalPercent}% | " +
                          $"{result.Points} | {((double)result.Points / result.GamesPlayed).ToString("F4", CultureInfo.InvariantCulture)} | " +
                          $"{expectation.ToString("F0", CultureInfo.InvariantCulture)} |");
    }

    report.AppendLine();
    report.AppendLine("## Победитель");
    report.AppendLine();
    report.AppendLine($"**P{winner.BetrayalPercent:D3} — {winner.BetrayalPercent}% предательств; {winner.Points} очков.**");
    report.AppendLine();
    report.AppendLine("## Выводы");
    report.AppendLine();
    report.AppendLine("1. При классической матрице выплат в однократной игре предательство строго доминирует сотрудничество: независимо от выбора соперника предательство приносит больше индивидуальных очков.");
    report.AppendLine($"2. В проведённом турнире больше всего очков набрала стратегия P{winner.BetrayalPercent:D3}. Теоретический оптимум по индивидуальному выигрышу — P100 (100% предательства).");
    report.AppendLine("3. Отличия фактических очков от математических ожиданий связаны с псевдослучайными выборками из 10 000 игр на пару. Одинаковое начальное зерно делает результат воспроизводимым.");
    report.AppendLine("4. Вывод относится только к однократной игре, заданной матрице выплат, одинаковому числу встреч со всеми другими стратегиями и индивидуальному критерию победы. При повторных играх или коллективном критерии результат может измениться.");
    report.AppendLine();
    report.AppendLine("## Проверка математического ожидания");
    report.AppendLine();
    report.AppendLine("Для игрока с вероятностью предательства `p` и соперника с вероятностью `q` (в долях единицы) ожидаемый балл одной игры равен:");
    report.AppendLine();
    report.AppendLine("`E(p, q) = 3(1 − p)(1 − q) + 5p(1 − q) + pq = 3 + 2p − 3q − pq`.");
    report.AppendLine();
    report.AppendLine("Столбец «Ожидаемые очки» — сумма `10 000 × E(p, q)` по всем 20 другим стратегиям (без самоигры). Для фиксированного `q` производная `dE/dp = 2 − q > 0`, поэтому P100 максимизирует ожидаемый индивидуальный счёт.");

    return report.ToString();
}

static double ExpectedTotal(int percentage, int gamesPerPair)
{
    double p = percentage / 100.0;
    double expected = 0;

    for (int opponentPercentage = 0; opponentPercentage <= 100; opponentPercentage += 5)
    {
        if (opponentPercentage == percentage)
        {
            continue;
        }

        double q = opponentPercentage / 100.0;
        expected += gamesPerPair * (3 * (1 - p) * (1 - q) + 5 * p * (1 - q) + p * q);
    }

    return expected;
}

internal readonly record struct Strategy(int BetrayalPercent)
{
    public bool Betrays(ref SplitMix64 random) => random.NextUInt64() % 100UL < (ulong)BetrayalPercent;
}

internal readonly record struct Result(int BetrayalPercent, long Points, int GamesPlayed);

// Explicit PRNG algorithm ensures the result does not depend on System.Random changes.
internal struct SplitMix64
{
    private ulong _state;

    public SplitMix64(ulong seed) => _state = seed;

    public ulong NextUInt64()
    {
        _state = unchecked(_state + 0x9E3779B97F4A7C15UL);
        ulong value = _state;
        value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
        value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL);
        return value ^ (value >> 31);
    }
}
