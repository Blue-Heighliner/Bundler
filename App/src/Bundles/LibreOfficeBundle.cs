namespace BlueHeighliner.Bundler;

/// <summary>
/// The LibreOffice office suite from the Ubuntu 24.04 archive: the word processor (Writer), spreadsheet (Calc), presentations (Impress),
/// drawing (Draw), formula editor (Math) and database front end (Base), with Base's embedded Firebird database, Python macros and the English
/// grammar checker. The installer USB drive puts every one of them on the computer except Base, so a bundle installed over it adds Base and brings
/// the others to the archive's newest version.
/// </summary>
internal sealed class LibreOfficeBundle(IAptBuilder apt, IAptInstaller installer, ILog log) : AptProgramBundle(apt, installer, log)
{
    /// <inheritdoc />
    public override string Name => "libre-office";

    /// <inheritdoc />
    public override string Description => "Write documents, spreadsheets, presentations, drawings, formulas and databases with LibreOffice.";

    /// <inheritdoc />
    public override int Order => 88;

    /// <inheritdoc />
    protected override string Package => "libreoffice-writer";

    /// <inheritdoc />
    protected override IReadOnlyList<string> Services { get; } = ["libreoffice-writer", "libreoffice-calc", "libreoffice-impress", "libreoffice-draw", "libreoffice-math", "libreoffice-base"];

    /// <inheritdoc />
    protected override IReadOnlyList<string> Support { get; } = ["libreoffice-sdbc-firebird", "libreoffice-script-provider-python", "libreoffice-lightproof-en"];
}
