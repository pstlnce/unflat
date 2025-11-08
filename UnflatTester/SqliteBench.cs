using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using System.Data;
using System.Diagnostics;
using Unflat;

namespace UnflatTester;

[ShortRunJob]
[MemoryDiagnoser]
[Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.FastestToSlowest)]
public class SqliteBench
{
    static SqliteBench()
    {
        SqlMapper.AddTypeHandler<Guid>(new GuildParser());
        SqlMapper.AddTypeHandler<TimeSpan>(new TimeSpanParser());
    }

    //[Benchmark]
    public async Task<List<Movie>> Dapper()
    {
        await using var connection = new SqliteConnection(SqliteData.ConnectionString);
        var result = await connection.QueryAsync<Movie, Director, Studio, BoxOfficeSummary, ReviewRating, GenreDetails, StreamingInfo, Movie>(
            "SELECT * FROM movies",
            map: (
                Movie movie,
                Director director,
                Studio studio,
                BoxOfficeSummary box,
                ReviewRating review,
                GenreDetails genre,
                StreamingInfo streaming
            ) =>
            {
                movie.MainDirector = director;
                movie.ProductionStudio = studio;
                movie.BoxOffice = box;
                movie.CriticRating = review;
                movie.Genre = genre;
                movie.CurrentStreamingInfo = streaming;

                return movie;
            },
            splitOn: $"{nameof(Director.Name)},{nameof(Studio.StudioName)},{nameof(BoxOfficeSummary.DomesticGross)},{nameof(ReviewRating.Score)},{nameof(GenreDetails.Primary)},{nameof(StreamingInfo.PlatformName)}"
        );

        return result.AsList();
    }

    [Benchmark]
    public async Task<int> Unflat()
    {
        await using var connection = new SqliteConnection(SqliteData.ConnectionString);
        //using var reader = await connection.ExecuteReaderAsync("SELECT * FROM movies");

        var openning = connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM movies;";

        command.Prepare();

        await openning;
        await using var reader = await command.ExecuteReaderAsync();

        var i = 0;
        foreach(var item in MovieParser.ReadUnbuffered(reader))
        {
            i += 1;
        }

        return i;
    }

    public sealed class GuildParser : SqlMapper.TypeHandler<Guid>
    {
        public override Guid Parse(object value)
        {
            return Guid.Parse((value as string)!);
        }

        public override void SetValue(IDbDataParameter parameter, Guid value)
        {
            throw new NotImplementedException();
        }
    }

    public sealed class TimeSpanParser : SqlMapper.TypeHandler<TimeSpan>
    {
        public override TimeSpan Parse(object value)
        {
            var seconds = (long)value;
            return TimeSpan.FromSeconds(seconds);
        }

        public override void SetValue(IDbDataParameter parameter, TimeSpan value)
        {
            throw new NotImplementedException();
        }
    }
}


/// <summary> Represents a movie with rich metadata including primitive and complex types. </summary>
[UnflatMarker]
public sealed class Movie
{
    // === Primitive Type Properties ===
    public required int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public required int ReleaseYear { get; set; }
    public bool IsReleased { get; set; }
    public required DateTime ReleaseDate { get; set; }
    public required decimal Budget { get; set; }
    public long TotalViews { get; set; }
    public short OscarWins { get; set; }
    public required Guid MovieIdentifier { get; set; }
    public decimal? InflationAdjustedGross { get; set; }
    public TimeSpan Runtime { get; set; }

    // === Complex Type Properties ===
    public required Director MainDirector { get; set; }
    public required Studio ProductionStudio { get; set; }
    public required BoxOfficeSummary? BoxOffice { get; set; }
    public required ReviewRating? CriticRating { get; set; }
    public required GenreDetails Genre { get; set; }
    public StreamingInfo? CurrentStreamingInfo { get; set; }

    [UnflatParser]
    public static bool ParseBool(object value)
    {
        return value is not DBNull and > 0;
    }

    [UnflatParser]
    public static TimeSpan ParseTimeSpan(object value)
    {
        var seconds = (long)value;
        return TimeSpan.FromSeconds(seconds);
    }

    [UnflatParser(CallFormat = $"{nameof(UnflatTester)}.{nameof(Movie)}.{nameof(Movie.ParseDateTime)}({{0}}, {{1}}, reader)")]
    public static DateTime ParseDateTime(object value, int column, IDataReader reader)
    {
        if(value is null or DBNull)
        {
            return DateTime.MinValue;
        }

        return DateTime.Parse((value as string)!);
    }

    [UnflatParser]
    public static Guid ParseGuid(object value)
    {
        return Guid.Parse((value as string)!);
    }
}

// === Complex Types (Classes used as properties) ===

public sealed class Director
{
    public required string Name { get; set; }
    public required DateTime DateOfBirth { get; set; }
    public string Nationality { get; set; } = string.Empty;
    public bool IsAcademyAwardWinner { get; set; }
    public int FilmCount { get; set; }
    public double AverageDirectorScore { get; set; }
}

public sealed class Studio
{
    public required string StudioName { get; set; }
    public required string HeadquartersCity { get; set; }
    public int FoundedYear { get; set; }
    public decimal AnnualRevenueUSD { get; set; }
    public required bool IsIndependent { get; set; }
    public required Guid StudioId { get; set; }
}

public sealed class BoxOfficeSummary
{
    public required decimal DomesticGross { get; set; }
    public required decimal InternationalGross { get; set; }
    public required decimal OpeningWeekendGross { get; set; }
    public DateTime HighestGrossingDate { get; set; }
    public bool BrokeRecord { get; set; }
    public long TicketSalesEstimate { get; set; }
}

public sealed class ReviewRating
{
    public double Score { get; set; }
    public int TotalReviews { get; set; }
    public string Source { get; set; } = string.Empty;
    public bool IsTopCriticPick { get; set; }
    public DateTime LastUpdated { get; set; }
}

public sealed class GenreDetails
{
    public required string Primary { get; set; }
    public string Secondary { get; set; } = string.Empty;
    public string Mood { get; set; } = string.Empty;
    public int PopularityIndex { get; set; }
    public required bool IsFranchiseEntry { get; set; }
}

public sealed class StreamingInfo
{
    public string PlatformName { get; set; } = string.Empty;
    public required DateTime AddedDate { get; set; }
    public required bool IsExclusive { get; set; }
    public decimal SubscriptionPrice { get; set; }
    public string RegionAvailability { get; set; } = string.Empty;
    public int? ViewCountInMillions { get; set; }
}

public class SqliteData
{
    public static string DBFile = Path.Combine(
        Directory.GetParent(
            Directory.GetParent(
                Directory.GetCurrentDirectory()
            )!.FullName
        )!.FullName
        ,"movie.db");

    public static string ConnectionString = $"Data Source=\"{DBFile}\";";

    public static void GenerateMeasured()
    {
        var start = Stopwatch.GetTimestamp();

        try
        {
            Generate();
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(start);
            Console.WriteLine("Generating Sqlite db instance took - {0}", elapsed);
        }
    }

    public static void Generate()
    {
        if(File.Exists(DBFile))
        {
            File.Delete(DBFile);
        }

        var seed = new Random(521);

        /*
        var datatable = new DataTable();
        datatable.Columns.AddRange([
            new(nameof(Movie.Id), typeof(int)),
            new(nameof(Movie.Title), typeof(string)),
            new(nameof(Movie.ReleaseYear), typeof(int)),
            new(nameof(Movie.IsReleased), typeof(bool)),
            new(nameof(Movie.ReleaseDate), typeof(DateTime)),
            new(nameof(Movie.Budget), typeof(decimal)),
            new(nameof(Movie.TotalViews), typeof(long)),
            new(nameof(Movie.OscarWins), typeof(short)),
            new(nameof(Movie.MovieIdentifier), typeof(Guid)),
            new(nameof(Movie.InflationAdjustedGross), typeof(decimal)),
            new(nameof(Movie.Runtime), typeof(TimeSpan)),

            new(nameof(Movie.MainDirector.Name), typeof(string)),
            new(nameof(Movie.MainDirector.DateOfBirth), typeof(DateTime)),
            new(nameof(Movie.MainDirector.Nationality), typeof(string)),
            new(nameof(Movie.MainDirector.IsAcademyAwardWinner), typeof(bool)),
            new(nameof(Movie.MainDirector.FilmCount), typeof(int)),
            new(nameof(Movie.MainDirector.AverageDirectorScore), typeof(double)),

            new(nameof(Movie.ProductionStudio.StudioName), typeof(string)),
            new(nameof(Movie.ProductionStudio.HeadquartersCity), typeof(string)),
            new(nameof(Movie.ProductionStudio.FoundedYear), typeof(int)),
            new(nameof(Movie.ProductionStudio.AnnualRevenueUSD), typeof(decimal)),
            new(nameof(Movie.ProductionStudio.IsIndependent), typeof(bool)),
            new(nameof(Movie.ProductionStudio.StudioId), typeof(Guid)),

            new(nameof(Movie.BoxOffice.DomesticGross), typeof(decimal)),
            new(nameof(Movie.BoxOffice.InternationalGross), typeof(decimal)),
            new(nameof(Movie.BoxOffice.OpeningWeekendGross), typeof(decimal)),
            new(nameof(Movie.BoxOffice.HighestGrossingDate), typeof(DateTime)),
            new(nameof(Movie.BoxOffice.BrokeRecord), typeof(bool)),
            new(nameof(Movie.BoxOffice.TicketSalesEstimate), typeof(long)),

            new(nameof(Movie.CriticRating.Score), typeof(double)),
            new(nameof(Movie.CriticRating.TotalReviews), typeof(int)),
            new(nameof(Movie.CriticRating.Source), typeof(string)),
            new(nameof(Movie.CriticRating.IsTopCriticPick), typeof(bool)),
            new(nameof(Movie.CriticRating.LastUpdated), typeof(DateTime)),

            new(nameof(Movie.Genre.Primary), typeof(string)),
            new(nameof(Movie.Genre.Secondary), typeof(string)),
            new(nameof(Movie.Genre.Mood), typeof(string)),
            new(nameof(Movie.Genre.PopularityIndex), typeof(int)),
            new(nameof(Movie.Genre.IsFranchiseEntry), typeof(bool)),

            new(nameof(Movie.CurrentStreamingInfo.PlatformName), typeof(string)),
            new(nameof(Movie.CurrentStreamingInfo.AddedDate), typeof(DateTime)),
            new(nameof(Movie.CurrentStreamingInfo.IsExclusive), typeof(bool)),
            new(nameof(Movie.CurrentStreamingInfo.SubscriptionPrice), typeof(decimal)),
            new(nameof(Movie.CurrentStreamingInfo.RegionAvailability), typeof(string)),
            new(nameof(Movie.CurrentStreamingInfo.ViewCountInMillions), typeof(int)),
        ]);
        */

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var tableCreation = connection.CreateCommand();
        tableCreation.CommandText = $"""
            CREATE TABLE movies (
                [{nameof(Movie.Id)}]                     INTEGER PRIMARY KEY ASC,
                [{nameof(Movie.Title)}]                  TEXT,
                [{nameof(Movie.ReleaseYear)}]            INTEGER,
                [{nameof(Movie.IsReleased)}]             INTEGER,
                [{nameof(Movie.ReleaseDate)}]            TEXT,
                [{nameof(Movie.Budget)}]                 REAL,
                [{nameof(Movie.TotalViews)}]             INTEGER,
                [{nameof(Movie.OscarWins)}]              INTEGER,
                [{nameof(Movie.MovieIdentifier)}]        TEXT,
                [{nameof(Movie.InflationAdjustedGross)}] REAL,
                [{nameof(Movie.Runtime)}]                INTEGER,

                [{nameof(Movie.MainDirector.Name)}]                 TEXT,
                [{nameof(Movie.MainDirector.DateOfBirth)}]          TEXT,
                [{nameof(Movie.MainDirector.Nationality)}]          TEXT,
                [{nameof(Movie.MainDirector.IsAcademyAwardWinner)}] INTEGER,
                [{nameof(Movie.MainDirector.FilmCount)}]            INTEGER,
                [{nameof(Movie.MainDirector.AverageDirectorScore)}] REAL,

                [{nameof(Movie.ProductionStudio.StudioName)}]       TEXT,
                [{nameof(Movie.ProductionStudio.HeadquartersCity)}] TEXT,
                [{nameof(Movie.ProductionStudio.FoundedYear)}]      INTEGER,
                [{nameof(Movie.ProductionStudio.AnnualRevenueUSD)}] REAL,
                [{nameof(Movie.ProductionStudio.IsIndependent)}]    INTEGER,
                [{nameof(Movie.ProductionStudio.StudioId)}]         TEXT,

                [{nameof(Movie.BoxOffice.DomesticGross)}]       REAL,
                [{nameof(Movie.BoxOffice.InternationalGross)}]  REAL,
                [{nameof(Movie.BoxOffice.OpeningWeekendGross)}] REAL,
                [{nameof(Movie.BoxOffice.HighestGrossingDate)}] TEXT,
                [{nameof(Movie.BoxOffice.BrokeRecord)}]         INTEGER,
                [{nameof(Movie.BoxOffice.TicketSalesEstimate)}] INTEGER,

                [{nameof(Movie.CriticRating.Score)}]           REAL,
                [{nameof(Movie.CriticRating.TotalReviews)}]    INTEGER,
                [{nameof(Movie.CriticRating.Source)}]          TEXT,
                [{nameof(Movie.CriticRating.IsTopCriticPick)}] INTEGER,
                [{nameof(Movie.CriticRating.LastUpdated)}]     TEXT,

                [{nameof(Movie.Genre.Primary)}]          TEXT,
                [{nameof(Movie.Genre.Secondary)}]        TEXT,
                [{nameof(Movie.Genre.Mood)}]             TEXT,
                [{nameof(Movie.Genre.PopularityIndex)}]  INTEGER,
                [{nameof(Movie.Genre.IsFranchiseEntry)}] INTEGER,

                [{nameof(Movie.CurrentStreamingInfo.PlatformName)}]        TEXT,
                [{nameof(Movie.CurrentStreamingInfo.AddedDate)}]           TEXT,
                [{nameof(Movie.CurrentStreamingInfo.IsExclusive)}]         INTEGER,
                [{nameof(Movie.CurrentStreamingInfo.SubscriptionPrice)}]   REAL,
                [{nameof(Movie.CurrentStreamingInfo.RegionAvailability)}]  TEXT,
                [{nameof(Movie.CurrentStreamingInfo.ViewCountInMillions)}] INTEGER
            );   
            """;

        tableCreation.ExecuteNonQuery();

        using var transact = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transact;

        command.CommandText = $"""
            INSERT INTO movies (
                [{nameof(Movie.Id)}],
                [{nameof(Movie.Title)}],
                [{nameof(Movie.ReleaseYear)}],
                [{nameof(Movie.IsReleased)}],
                [{nameof(Movie.ReleaseDate)}],
                [{nameof(Movie.Budget)}],
                [{nameof(Movie.TotalViews)}],
                [{nameof(Movie.OscarWins)}],
                [{nameof(Movie.MovieIdentifier)}],
                [{nameof(Movie.InflationAdjustedGross)}],
                [{nameof(Movie.Runtime)}],

                [{nameof(Movie.MainDirector.Name)}],
                [{nameof(Movie.MainDirector.DateOfBirth)}],
                [{nameof(Movie.MainDirector.Nationality)}],
                [{nameof(Movie.MainDirector.IsAcademyAwardWinner)}],
                [{nameof(Movie.MainDirector.FilmCount)}],
                [{nameof(Movie.MainDirector.AverageDirectorScore)}],

                [{nameof(Movie.ProductionStudio.StudioName)}],
                [{nameof(Movie.ProductionStudio.HeadquartersCity)}],
                [{nameof(Movie.ProductionStudio.FoundedYear)}],
                [{nameof(Movie.ProductionStudio.AnnualRevenueUSD)}],
                [{nameof(Movie.ProductionStudio.IsIndependent)}],
                [{nameof(Movie.ProductionStudio.StudioId)}],

                [{nameof(Movie.BoxOffice.DomesticGross)}],
                [{nameof(Movie.BoxOffice.InternationalGross)}],
                [{nameof(Movie.BoxOffice.OpeningWeekendGross)}],
                [{nameof(Movie.BoxOffice.HighestGrossingDate)}],
                [{nameof(Movie.BoxOffice.BrokeRecord)}],
                [{nameof(Movie.BoxOffice.TicketSalesEstimate)}],

                [{nameof(Movie.CriticRating.Score)}],
                [{nameof(Movie.CriticRating.TotalReviews)}],
                [{nameof(Movie.CriticRating.Source)}],
                [{nameof(Movie.CriticRating.IsTopCriticPick)}],
                [{nameof(Movie.CriticRating.LastUpdated)}],

                [{nameof(Movie.Genre.Primary)}],
                [{nameof(Movie.Genre.Secondary)}],
                [{nameof(Movie.Genre.Mood)}],
                [{nameof(Movie.Genre.PopularityIndex)}],
                [{nameof(Movie.Genre.IsFranchiseEntry)}],

                [{nameof(Movie.CurrentStreamingInfo.PlatformName)}],
                [{nameof(Movie.CurrentStreamingInfo.AddedDate)}],
                [{nameof(Movie.CurrentStreamingInfo.IsExclusive)}],
                [{nameof(Movie.CurrentStreamingInfo.SubscriptionPrice)}],
                [{nameof(Movie.CurrentStreamingInfo.RegionAvailability)}],
                [{nameof(Movie.CurrentStreamingInfo.ViewCountInMillions)}]
            )
            VALUES (
                ${nameof(Movie.Id)},
                ${nameof(Movie.Title)},
                ${nameof(Movie.ReleaseYear)},
                ${nameof(Movie.IsReleased)},
                ${nameof(Movie.ReleaseDate)},
                ${nameof(Movie.Budget)},
                ${nameof(Movie.TotalViews)},
                ${nameof(Movie.OscarWins)},
                ${nameof(Movie.MovieIdentifier)},
                ${nameof(Movie.InflationAdjustedGross)},
                ${nameof(Movie.Runtime)},

                ${nameof(Movie.MainDirector.Name)},
                ${nameof(Movie.MainDirector.DateOfBirth)},
                ${nameof(Movie.MainDirector.Nationality)},
                ${nameof(Movie.MainDirector.IsAcademyAwardWinner)},
                ${nameof(Movie.MainDirector.FilmCount)},
                ${nameof(Movie.MainDirector.AverageDirectorScore)},

                ${nameof(Movie.ProductionStudio.StudioName)},
                ${nameof(Movie.ProductionStudio.HeadquartersCity)},
                ${nameof(Movie.ProductionStudio.FoundedYear)},
                ${nameof(Movie.ProductionStudio.AnnualRevenueUSD)},
                ${nameof(Movie.ProductionStudio.IsIndependent)},
                ${nameof(Movie.ProductionStudio.StudioId)},

                ${nameof(Movie.BoxOffice.DomesticGross)},
                ${nameof(Movie.BoxOffice.InternationalGross)},
                ${nameof(Movie.BoxOffice.OpeningWeekendGross)},
                ${nameof(Movie.BoxOffice.HighestGrossingDate)},
                ${nameof(Movie.BoxOffice.BrokeRecord)},
                ${nameof(Movie.BoxOffice.TicketSalesEstimate)},

                ${nameof(Movie.CriticRating.Score)},
                ${nameof(Movie.CriticRating.TotalReviews)},
                ${nameof(Movie.CriticRating.Source)},
                ${nameof(Movie.CriticRating.IsTopCriticPick)},
                ${nameof(Movie.CriticRating.LastUpdated)},

                ${nameof(Movie.Genre.Primary)},
                ${nameof(Movie.Genre.Secondary)},
                ${nameof(Movie.Genre.Mood)},
                ${nameof(Movie.Genre.PopularityIndex)},
                ${nameof(Movie.Genre.IsFranchiseEntry)},

                ${nameof(Movie.CurrentStreamingInfo.PlatformName)},
                ${nameof(Movie.CurrentStreamingInfo.AddedDate)},
                ${nameof(Movie.CurrentStreamingInfo.IsExclusive)},
                ${nameof(Movie.CurrentStreamingInfo.SubscriptionPrice)},
                ${nameof(Movie.CurrentStreamingInfo.RegionAvailability)},
                ${nameof(Movie.CurrentStreamingInfo.ViewCountInMillions)}
            );
            """;

        command.Parameters.AddRange((IEnumerable<SqliteParameter>)[
            new(nameof(Movie.Id), SqliteType.Integer),
            new(nameof(Movie.Title), SqliteType.Text, 100),
            new(nameof(Movie.ReleaseYear), SqliteType.Integer),
            new(nameof(Movie.IsReleased), SqliteType.Integer),
            new(nameof(Movie.ReleaseDate), SqliteType.Text, size: 20),
            new(nameof(Movie.Budget), SqliteType.Real),
            new(nameof(Movie.TotalViews), SqliteType.Integer),
            new(nameof(Movie.OscarWins), SqliteType.Integer),
            new(nameof(Movie.MovieIdentifier), SqliteType.Text, size: 50),
            new(nameof(Movie.InflationAdjustedGross), SqliteType.Real),
            new(nameof(Movie.Runtime), SqliteType.Integer), // store seconds

            new(nameof(Movie.MainDirector.Name), SqliteType.Text, size: 100),
            new(nameof(Movie.MainDirector.DateOfBirth), SqliteType.Text, size: 50),
            new(nameof(Movie.MainDirector.Nationality), SqliteType.Text, size: 50),
            new(nameof(Movie.MainDirector.IsAcademyAwardWinner), SqliteType.Integer),
            new(nameof(Movie.MainDirector.FilmCount), SqliteType.Integer),
            new(nameof(Movie.MainDirector.AverageDirectorScore), SqliteType.Real),

            new(nameof(Movie.ProductionStudio.StudioName), SqliteType.Text, size: 50),
            new(nameof(Movie.ProductionStudio.HeadquartersCity), SqliteType.Text, size: 50),
            new(nameof(Movie.ProductionStudio.FoundedYear), SqliteType.Integer),
            new(nameof(Movie.ProductionStudio.AnnualRevenueUSD), SqliteType.Real),
            new(nameof(Movie.ProductionStudio.IsIndependent), SqliteType.Integer),
            new(nameof(Movie.ProductionStudio.StudioId), SqliteType.Text, size : 50),

            new(nameof(Movie.BoxOffice.DomesticGross), SqliteType.Real),
            new(nameof(Movie.BoxOffice.InternationalGross), SqliteType.Real),
            new(nameof(Movie.BoxOffice.OpeningWeekendGross), SqliteType.Real),
            new(nameof(Movie.BoxOffice.HighestGrossingDate), SqliteType.Text, size : 50),
            new(nameof(Movie.BoxOffice.BrokeRecord), SqliteType.Integer),
            new(nameof(Movie.BoxOffice.TicketSalesEstimate), SqliteType.Integer),

            new(nameof(Movie.CriticRating.Score), SqliteType.Real),
            new(nameof(Movie.CriticRating.TotalReviews), SqliteType.Integer),
            new(nameof(Movie.CriticRating.Source), SqliteType.Text, size: 100),
            new(nameof(Movie.CriticRating.IsTopCriticPick), SqliteType.Integer),
            new(nameof(Movie.CriticRating.LastUpdated), SqliteType.Text, size : 50),

            new(nameof(Movie.Genre.Primary), SqliteType.Text, size: 100),
            new(nameof(Movie.Genre.Secondary), SqliteType.Text, size: 100),
            new(nameof(Movie.Genre.Mood), SqliteType.Text, size: 100),
            new(nameof(Movie.Genre.PopularityIndex), SqliteType.Integer),
            new(nameof(Movie.Genre.IsFranchiseEntry), SqliteType.Integer),

            new(nameof(Movie.CurrentStreamingInfo.PlatformName), SqliteType.Text, size: 50),
            new(nameof(Movie.CurrentStreamingInfo.AddedDate), SqliteType.Text, size : 50),
            new(nameof(Movie.CurrentStreamingInfo.IsExclusive), SqliteType.Integer),
            new(nameof(Movie.CurrentStreamingInfo.SubscriptionPrice), SqliteType.Real),
            new(nameof(Movie.CurrentStreamingInfo.RegionAvailability), SqliteType.Text, size: 50),
            new(nameof(Movie.CurrentStreamingInfo.ViewCountInMillions), SqliteType.Integer),
        ]);

        command.Prepare();

        var parameters = command.Parameters;

        for (var i = 0; i < 1000_000; i++)
        {
            var movie = GenerateRandomMovie(i, seed);

            parameters[0].Value = movie.Id;
            parameters[1].Value = movie.Title ?? (object)DBNull.Value;
            parameters[2].Value = movie.ReleaseYear;
            parameters[3].Value = movie.IsReleased ? 1 : 0; // bool → SQLite int
            parameters[4].Value = movie.ReleaseDate; // string in ISO format
            parameters[5].Value = movie.Budget;
            parameters[6].Value = movie.TotalViews;
            parameters[7].Value = movie.OscarWins;
            parameters[8].Value = movie.MovieIdentifier.ToString();
            parameters[9].Value = movie.InflationAdjustedGross ?? (object)DBNull.Value;
            parameters[10].Value = movie.Runtime.TotalSeconds;

            // MainDirector
            parameters[11].Value = movie.MainDirector?.Name ?? (object)DBNull.Value;
            parameters[12].Value = movie.MainDirector?.DateOfBirth ?? (object)DBNull.Value; // string
            parameters[13].Value = movie.MainDirector?.Nationality ?? (object)DBNull.Value;
            parameters[14].Value = movie.MainDirector?.IsAcademyAwardWinner switch
            {
                null => (object)DBNull.Value,
                true => 1,
                false => 0
            };
            parameters[15].Value = movie.MainDirector?.FilmCount ?? (object)DBNull.Value;
            parameters[16].Value = movie.MainDirector?.AverageDirectorScore ?? (object)DBNull.Value;

            // ProductionStudio
            parameters[17].Value = movie.ProductionStudio?.StudioName ?? (object)DBNull.Value;
            parameters[18].Value = movie.ProductionStudio?.HeadquartersCity ?? (object)DBNull.Value;
            parameters[19].Value = movie.ProductionStudio?.FoundedYear ?? (object)DBNull.Value;
            parameters[20].Value = movie.ProductionStudio?.AnnualRevenueUSD ?? (object)DBNull.Value;
            parameters[21].Value = movie.ProductionStudio?.IsIndependent switch
            {
                null => (object)DBNull.Value,
                true => 1,
                false => 0
            };
            parameters[22].Value = movie.ProductionStudio?.StudioId.ToString() ?? (object)DBNull.Value;

            // BoxOffice
            parameters[23].Value = movie.BoxOffice?.DomesticGross ?? (object)DBNull.Value;
            parameters[24].Value = movie.BoxOffice?.InternationalGross ?? (object)DBNull.Value;
            parameters[25].Value = movie.BoxOffice?.OpeningWeekendGross ?? (object)DBNull.Value;
            parameters[26].Value = movie.BoxOffice?.HighestGrossingDate ?? (object)DBNull.Value; // string
            parameters[27].Value = movie.BoxOffice?.BrokeRecord switch
            {
                null => (object)DBNull.Value,
                true => 1,
                false => 0
            };
            parameters[28].Value = movie.BoxOffice?.TicketSalesEstimate ?? (object)DBNull.Value;

            // CriticRating
            parameters[29].Value = movie.CriticRating?.Score ?? (object)DBNull.Value;
            parameters[30].Value = movie.CriticRating?.TotalReviews ?? (object)DBNull.Value;
            parameters[31].Value = movie.CriticRating?.Source ?? (object)DBNull.Value;
            parameters[32].Value = movie.CriticRating?.IsTopCriticPick switch
            {
                null => (object)DBNull.Value,
                true => 1,
                false => 0
            };
            parameters[33].Value = movie.CriticRating?.LastUpdated ?? (object)DBNull.Value;

            // Genre
            parameters[34].Value = movie.Genre?.Primary ?? (object)DBNull.Value;
            parameters[35].Value = movie.Genre?.Secondary ?? (object)DBNull.Value;
            parameters[36].Value = movie.Genre?.Mood ?? (object)DBNull.Value;
            parameters[37].Value = movie.Genre?.PopularityIndex ?? (object)DBNull.Value;
            parameters[38].Value = movie.Genre?.IsFranchiseEntry switch
            {
                null => (object)DBNull.Value,
                true => 1,
                false => 0
            };

            // CurrentStreamingInfo
            parameters[39].Value = movie.CurrentStreamingInfo?.PlatformName ?? (object)DBNull.Value;
            parameters[40].Value = movie.CurrentStreamingInfo?.AddedDate ?? (object)DBNull.Value;
            parameters[41].Value = movie.CurrentStreamingInfo?.IsExclusive switch
            {
                null => (object)DBNull.Value,
                true => 1,
                false => 0
            };

            parameters[42].Value = movie.CurrentStreamingInfo?.SubscriptionPrice ?? (object)DBNull.Value;
            parameters[43].Value = movie.CurrentStreamingInfo?.RegionAvailability ?? (object)DBNull.Value;
            parameters[44].Value = movie.CurrentStreamingInfo?.ViewCountInMillions ?? (object)DBNull.Value;

            command.ExecuteNonQuery();
        }

        transact.Commit();
    }

    public static Movie GenerateRandomMovie(int id, Random seed)
    {
        return new Movie
        {
            Id = id + 1,
            Title = GenerateRandomTitle(seed),
            ReleaseYear = seed.Next(1920, 2026),
            IsReleased = seed.NextDouble() > 0.1, // 90% released
            ReleaseDate = GenerateRandomDateTime(seed, new DateTime(1920, 1, 1), new DateTime(2025, 12, 31)),
            Budget = (decimal)(seed.NextDouble() * 250_000_000), // up to $250M
            TotalViews = seed.NextInt64(0, 1_500_000_000),
            OscarWins = (short)seed.Next(0, 4), // 0 to 3 wins
            MovieIdentifier = Guid.NewGuid(),
            InflationAdjustedGross = seed.NextDouble() > 0.3 ? (decimal?)(seed.NextDouble() * 500_000_000) : null,
            Runtime = TimeSpan.FromMinutes(seed.Next(60, 240)),

            MainDirector = new Director
            {
                Name = GenerateRandomName(seed),
                DateOfBirth = GenerateRandomDateTime(seed, new DateTime(1940, 1, 1), new DateTime(2000, 12, 31)),
                Nationality = GenerateRandomCountry(seed),
                IsAcademyAwardWinner = seed.NextDouble() < 0.15,
                FilmCount = seed.Next(1, 50),
                AverageDirectorScore = seed.NextDouble() * 9.9
            },

            ProductionStudio = new Studio
            {
                StudioName = GenerateRandomStudioName(seed),
                HeadquartersCity = GenerateRandomCity(seed),
                FoundedYear = seed.Next(1920, 2005),
                AnnualRevenueUSD = (decimal)(seed.NextDouble() * 2_000_000_000),
                IsIndependent = seed.NextDouble() < 0.3,
                StudioId = Guid.NewGuid()
            },

            BoxOffice = seed.NextDouble() > 0.2 ? new BoxOfficeSummary
            {
                DomesticGross = (decimal)(seed.NextDouble() * 200_000_000),
                InternationalGross = (decimal)(seed.NextDouble() * 500_000_000),
                OpeningWeekendGross = (decimal)(seed.NextDouble() * 100_000_000),
                HighestGrossingDate = GenerateRandomDateTime(seed, new DateTime(1920, 1, 1), new DateTime(2025, 12, 31)),
                BrokeRecord = seed.NextDouble() < 0.05,
                TicketSalesEstimate = seed.NextInt64(100_000, 50_000_000)
            } : null,
            
            CriticRating = seed.NextDouble() > 0.15 ? new ReviewRating
            {
                Score = seed.NextDouble() * 10.0,
                TotalReviews = seed.Next(10, 25000),
                Source = PickRandom(seed, ["Rotten Tomatoes", "Metacritic", "IMDb", "CinemaScore", "FilmAffinity"]),
                IsTopCriticPick = seed.NextDouble() < 0.25,
                LastUpdated = GenerateRandomDateTime(seed, new DateTime(2020, 1, 1), new DateTime(2025, 12, 31))
            } : null,

            Genre = new GenreDetails
            {
                Primary = PickRandom(seed, ["Action", "Drama", "Comedy", "Horror", "Sci-Fi", "Romance", "Thriller", "Fantasy"]),
                Secondary = PickRandom(seed, ["Mystery", "Adventure", "Crime", "Animation", "Musical", "Documentary", "Western"]),
                Mood = PickRandom(seed, ["Dark", "Hopeful", "Tense", "Whimsical", "Epic", "Melancholic", "Satirical"]),
                PopularityIndex = seed.Next(1, 1000),
                IsFranchiseEntry = seed.NextDouble() < 0.4
            },

            CurrentStreamingInfo = seed.NextDouble() > 0.3 ? new StreamingInfo
            {
                PlatformName = PickRandom(seed, ["Netflix", "Amazon Prime", "Hulu", "Disney+", "HBO Max", "Apple TV+", "Paramount+", "Peacock"]),
                AddedDate = GenerateRandomDateTime(seed, new DateTime(2015, 1, 1), new DateTime(2025, 12, 31)),
                IsExclusive = seed.NextDouble() < 0.6,
                SubscriptionPrice = (decimal)(seed.NextDouble() * 15.99),
                RegionAvailability = PickRandom(seed, ["Global", "North America", "US Only", "Europe", "Asia-Pacific", "LATAM"]),
                ViewCountInMillions = seed.NextDouble() > 0.2 ? (int?)seed.Next(1, 500) : null
            } : null
        };
    }

    static string[] _adjectives = ["Eternal", "Final", "Lost", "Dark", "Infinite", "Shadow", "Red", "Silent", "Crimson", "Frozen", "Quantum", "Midnight", "Rising", "Last", "Ancient"];

    static string[] _nouns = ["Empire", "Journey", "Kingdom", "Horizon", "Legacy", "Reckoning", "Saga", "Chronicles", "Frontier", "Prophecy", "Odyssey", "Gambit", "Uprising", "Fate", "Veil"];

    static string[] _styles = ["", ": The Awakening", ": Rise of the Fallen", " 3D", ": Revisited", ": Ultimate Cut", ": Director's Cut"];
    
    private static string GenerateRandomTitle(Random seed)
    {
        return $"{PickRandom(seed, _adjectives)} {PickRandom(seed, _nouns)}{PickRandom(seed, _styles)}";
    }

    private static string GenerateRandomName(Random seed)
    {
        return $"{PickRandom(seed, ["James", "Christopher", "Natalie", "Lena", "David", "Emma", "Michael", "Sophia", "Andrei", "Yuki", "Carlos", "Amina", "Lars", "Mei", "Kwame"])} {PickRandom(seed, ["Smith", "Johnson", "Ishikawa", "Dubois", "Garcia", "Müller", "Nguyen", "Silva", "Patel", "Kim", "O'Hara", "Al-Farid", "van Dyk", "Rossi", "Sato"])}";
    }

    private static string GenerateRandomStudioName(Random seed)
    {
        return $"{PickRandom(seed, ["Silver", "Golden", "Apex", "Nova", "Cinematic", "Stellar", "Epic", "Vision", "Horizon", "Summit"])} {PickRandom(seed, ["Pictures", "Studios", "Films", "Entertainment", "Motion", "Releases", "Cinema Group"])}";
    }

    private static string GenerateRandomCity(Random seed)
    {
        return PickRandom(seed, [ "Los Angeles", "London", "Tokyo", "Toronto", "Berlin", "Sydney", "Mumbai", "Paris", "Seoul", "Barcelona", "Cape Town", "Mexico City" ]);
    }

    private static string GenerateRandomCountry(Random seed)
    {
        return PickRandom(seed, ["USA", "UK", "Canada", "Germany", "Japan", "France", "South Korea", "Australia", "Italy", "Brazil", "India", "Sweden"]);
    }

    private static T PickRandom<T>(Random seed, params ReadOnlySpan<T> array)
    {
        return array[seed.Next(array.Length)];
    }

    private static DateTime GenerateRandomDateTime(Random seed, DateTime start, DateTime end)
    {
        var range = end - start;
        var randomDays = seed.NextDouble() * range.TotalDays;
        return start.AddDays(randomDays).AddHours(seed.Next(0, 24))
                                   .AddMinutes(seed.Next(0, 60))
                                   .AddSeconds(seed.Next(0, 60));
    }
}
