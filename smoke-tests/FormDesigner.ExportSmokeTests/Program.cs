using FormDesigner.DesignerSystem.Binding;
using FormDesigner.DesignerSystem.BuiltIn;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FormDesigner.DesignerSystem.Infrastructure;
using FormDesigner.DesignerSystem.Hosting;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using AvaloniaDesigner.Host.Protocol;
using AvaloniaDesigner.VsHost;
using FormDesigner.Localization;
using FormDesigner.Models;
using FormDesigner.PluginContracts;
using FormDesigner.Services;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using System.Collections;
using System.Diagnostics;
using System.IO.Compression;
using System.IO.Pipes;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static class Program
{
    private const string AvaloniaVersion = "11.1.5";
    private const string AvaloniaDesktopVersion = "11.1.5";
    private const int SmokeRunsToKeep = 5;
    private static readonly Dictionary<string, string> LinqToSqlSmokeDllCache = new(StringComparer.OrdinalIgnoreCase);
    private static bool _avaloniaRuntimeInitialized;

    [STAThread]
    public static int Main(string[] args)
    {
        var artifactsRoot = args.Length > 0
            ? Path.GetFullPath(args[0])
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "smoke-tests"));

        Directory.CreateDirectory(artifactsRoot);
        var runRoot = Path.Combine(artifactsRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(runRoot);
        File.WriteAllText(Path.Combine(artifactsRoot, "latest-run.txt"), runRoot, Encoding.UTF8);
        Console.WriteLine($"Run artifacts: {runRoot}");

        var scenarios = new SmokeScenario[]
        {
            new("LocalizationKeysExistForMainUi", ConfigureSimpleFormExport, AssertLocalizationKeysExistForMainUi),
            new("PropertyNamesAreNotLocalized", ConfigureSimpleFormExport, AssertPropertyNamesAreNotLocalized),
            new("ControlNamesAreNotLocalized", ConfigureSimpleFormExport, AssertControlNamesAreNotLocalized),
            new("RussianUiTextAppliedToMainButtons", ConfigureSimpleFormExport, AssertRussianUiTextAppliedToMainButtons),
            new("MissingLocalizationFallsBackToEnglishOrKey", ConfigureSimpleFormExport, AssertMissingLocalizationFallsBackToEnglishOrKey),
            new("PropertyDescriptionProviderHasRussianDescriptions", ConfigureSimpleFormExport, AssertPropertyDescriptionProviderHasRussianDescriptions),
            new("PropertyInspectorUsesRussianPropertyDescriptionTooltips", ConfigurePropertyInspectorUsesRussianPropertyDescriptionTooltips, AssertPropertyInspectorUsesRussianPropertyDescriptionTooltips),
            new("PropertyInspectorEnumEditorsUseComboBox", ConfigurePropertyInspectorEnumEditorsUseComboBox, AssertPropertyInspectorEnumEditorsUseComboBox),
            new("LayoutPropertiesAvailableInPropertyInspector", ConfigureLayoutPropertiesAvailableInPropertyInspector, AssertLayoutPropertiesAvailableInPropertyInspector),
            new("LayoutTabCanBeHiddenWithoutBreakingProperties", ConfigureLayoutPropertiesAvailableInPropertyInspector, AssertLayoutTabCanBeHiddenWithoutBreakingProperties),
            new("RuntimeBadgeDoesNotAffectPreviewBounds", ConfigureRuntimeBadgeDoesNotAffectPreviewBounds, AssertRuntimeBadgeDoesNotAffectPreviewBounds),
            new("HelpWindowOpensMaximized", ConfigureSimpleFormExport, AssertHelpWindowOpensMaximized),
            new("HelpWindowOpensLargeAdaptive", ConfigureSimpleFormExport, AssertHelpWindowOpensLargeAdaptive),
            new("HelpWindowReusesExistingInstance", ConfigureSimpleFormExport, AssertHelpWindowReusesExistingInstance),
            new("HelpSectionsExist", ConfigureSimpleFormExport, AssertHelpSectionsExist),
            new("HelpCarouselHasSlides", ConfigureSimpleFormExport, AssertHelpCarouselHasSlides),
            new("HelpContentMentionsAlpha30", ConfigureSimpleFormExport, AssertHelpContentMentionsAlpha30),
            new("HelpAboutProjectDoesNotMentionUniversityPresentation", ConfigureSimpleFormExport, AssertHelpAboutProjectDoesNotMentionUniversityPresentation),
            new("HelpDeveloperSectionExists", ConfigureSimpleFormExport, AssertHelpDeveloperSectionExists),
            new("HelpDeveloperSectionContainsCoreTerms", ConfigureSimpleFormExport, AssertHelpDeveloperSectionContainsCoreTerms),
            new("HelpSearchFindsDeveloperTerms", ConfigureSimpleFormExport, AssertHelpSearchFindsDeveloperTerms),
            new("HelpNavigationChangesSelectedSection", ConfigureSimpleFormExport, AssertHelpNavigationChangesSelectedSection),
            new("HelpLayoutWorksInMaximizedMode", ConfigureSimpleFormExport, AssertHelpLayoutWorksInMaximizedMode),
            new("HelpWindowDoesNotBlockMainDesigner", ConfigureSimpleFormExport, AssertHelpWindowDoesNotBlockMainDesigner),
            new("SettingsButtonOpensSettingsWindow", ConfigureSimpleFormExport, AssertSettingsButtonOpensSettingsWindow),
            new("SettingsWindowReusesExistingInstance", ConfigureSimpleFormExport, AssertSettingsWindowReusesExistingInstance),
            new("ExportSettingsMovedOutOfExportPanel", ConfigureSimpleFormExport, AssertExportSettingsMovedOutOfExportPanel),
            new("SettingsSectionsExist", ConfigureSimpleFormExport, AssertSettingsSectionsExist),
            new("SqlSettingsBuildWindowsAuthConnectionString", ConfigureSimpleFormExport, AssertSqlSettingsBuildWindowsAuthConnectionString),
            new("SqlSettingsBuildSqlLoginConnectionString", ConfigureSimpleFormExport, AssertSqlSettingsBuildSqlLoginConnectionString),
            new("DataSqlPanelUsesGlobalSettings", ConfigureSimpleFormExport, AssertDataSqlPanelUsesGlobalSettings),
            new("DataSqlPanelShowsWarningWhenGlobalSqlMissing", ConfigureSimpleFormExport, AssertDataSqlPanelShowsWarningWhenGlobalSqlMissing),
            new("NuGetSettingsPersist", ConfigureSimpleFormExport, AssertNuGetSettingsPersist),
            new("SettingsPersistBetweenRuns", ConfigureSimpleFormExport, AssertSettingsPersistBetweenRuns),
            new("SettingsLanguageApplyUpdatesSettingsWindow", ConfigureSimpleFormExport, AssertSettingsLanguageApplyUpdatesSettingsWindow),
            new("SettingsLanguagePersistsAfterRestart", ConfigureSimpleFormExport, AssertSettingsLanguagePersistsAfterRestart),
            new("SettingsButtonsLocalized", ConfigureSimpleFormExport, AssertSettingsButtonsLocalized),
            new("SettingsUnsavedIndicatorIsHumanReadable", ConfigureSimpleFormExport, AssertSettingsUnsavedIndicatorIsHumanReadable),
            new("SettingsCancelDiscardsDraftChanges", ConfigureSimpleFormExport, AssertSettingsCancelDiscardsDraftChanges),
            new("SettingsApplyKeepsWindowOpenAndAppliesValues", ConfigureSimpleFormExport, AssertSettingsApplyKeepsWindowOpenAndAppliesValues),
            new("SettingsSavePersistsValues", ConfigureSimpleFormExport, AssertSettingsSavePersistsValues),
            new("SettingsNoDeadInteractiveOptions", ConfigureSimpleFormExport, AssertSettingsNoDeadInteractiveOptions),
            new("SettingsSqlWindowsAuthHidesCredentials", ConfigureSimpleFormExport, AssertSettingsSqlWindowsAuthHidesCredentials),
            new("SettingsNugetSectionLocalizedAndPersists", ConfigureSimpleFormExport, AssertSettingsNugetSectionLocalizedAndPersists),
            new("SettingsContentScrollViewerHasFiniteViewport", ConfigureSimpleFormExport, AssertSettingsContentScrollViewerHasFiniteViewport),
            new("SettingsFooterOutsideScrollableContent", ConfigureSimpleFormExport, AssertSettingsFooterOutsideScrollableContent),
            new("SettingsSectionChangeResetsContentScroll", ConfigureSimpleFormExport, AssertSettingsSectionChangeResetsContentScroll),
            new("ValidateBuildShowsRestoreAndBuildSteps", ConfigureSimpleFormExport, AssertValidateBuildShowsRestoreAndBuildSteps),
            new("ValidateBuildStoresDetailedLogs", ConfigureSimpleFormExport, AssertValidateBuildStoresDetailedLogs),
            new("ValidateBuildSettingsCanDisableAutoBuild", ConfigureSimpleFormExport, AssertValidateBuildSettingsCanDisableAutoBuild),
            new("ValidateBuildUsesDefaultNugetSourceWhenCustomEmpty", ConfigureSimpleFormExport, AssertValidateBuildUsesDefaultNugetSourceWhenCustomEmpty),
            new("ValidateBuildUsesCustomHttpsNugetSource", ConfigureSimpleFormExport, AssertValidateBuildUsesCustomHttpsNugetSource),
            new("ValidateBuildUsesCustomHttpNugetSourceWithAllowInsecure", ConfigureSimpleFormExport, AssertValidateBuildUsesCustomHttpNugetSourceWithAllowInsecure),
            new("ValidateBuildBlocksHttpSourceWithoutAllowInsecure", ConfigureSimpleFormExport, AssertValidateBuildBlocksHttpSourceWithoutAllowInsecure),
            new("NugetSourceSettingsPersist", ConfigureSimpleFormExport, AssertNugetSourceSettingsPersist),
            new("ValidateBuildOutputShowsNugetSource", ConfigureSimpleFormExport, AssertValidateBuildOutputShowsNugetSource),
            new("LogsPanelShowsExportBuildDllErrors", ConfigureSimpleFormExport, AssertLogsPanelShowsExportBuildDllErrors),
            new("SimpleFormExport", ConfigureSimpleFormExport, AssertSimpleFormExport),
            new("CanvasBorderBackgroundZOrderExport", ConfigureCanvasBorderBackgroundZOrderExport, AssertCanvasBorderBackgroundZOrderExport),
            new("PreviewAndExportUseSameControlOrder", ConfigurePreviewExportConsistencyForm, AssertPreviewAndExportUseSameControlOrder),
            new("BorderDoesNotCoverTextControlsAfterExport", ConfigurePreviewExportConsistencyForm, AssertBorderDoesNotCoverTextControlsAfterExport),
            new("PreviewAndExportHaveSameBounds", ConfigurePreviewExportBoundsForm, AssertPreviewAndExportHaveSameBounds),
            new("PreviewAndExportHaveSameKeyProperties", ConfigurePreviewExportPropertiesForm, AssertPreviewAndExportHaveSameKeyProperties),
            new("PreviewGenerationDoesNotMutateEditorState", ConfigurePreviewExportStateMutationForm, AssertPreviewGenerationDoesNotMutateEditorState),
            new("AxamlPreviewSettingsAreAvailable", ConfigureSimpleFormExport, AssertAxamlPreviewSettingsAreAvailable),
            new("AxamlPreviewUsesExportGenerator", ConfigurePreviewExportStateMutationForm, AssertAxamlPreviewUsesExportGenerator),
            new("AxamlPreviewDoesNotMutateEditorState", ConfigurePreviewExportStateMutationForm, AssertAxamlPreviewDoesNotMutateEditorState),
            new("AxamlPreviewLoaderUsesAvaloniaXamlLoader", ConfigureSimpleFormExport, AssertAxamlPreviewLoaderUsesAvaloniaXamlLoader),
            new("RuntimePreviewDoesNotUsePrecompiledResourceLoaderForTempFile", ConfigureSimpleFormExport, AssertRuntimePreviewDoesNotUsePrecompiledResourceLoaderForTempFile),
            new("RuntimePreviewAxamlHasNoExportXClass", ConfigurePreviewExportStateMutationForm, AssertRuntimePreviewAxamlHasNoExportXClass),
            new("RuntimePreviewUserControlDoesNotContainWindowOnlyProperties", ConfigurePreviewExportStateMutationForm, AssertRuntimePreviewUserControlDoesNotContainWindowOnlyProperties),
            new("ExportWindowStillContainsWindowProperties", ConfigurePreviewExportStateMutationForm, AssertExportWindowStillContainsWindowProperties),
            new("RuntimePreviewThemeAppliedByHost", ConfigureSimpleFormExport, AssertRuntimePreviewThemeAppliedByHost),
            new("RuntimePreviewLoadsAxamlFromStringOrStream", ConfigureSimpleFormExport, AssertRuntimePreviewLoadsAxamlFromStringOrStream),
            new("RuntimePreviewFailureShowsUsefulDiagnostics", ConfigureSimpleFormExport, AssertRuntimePreviewFailureShowsUsefulDiagnostics),
            new("ExportGenerationDoesNotMutateEditorState", ConfigurePreviewExportStateMutationForm, AssertExportGenerationDoesNotMutateEditorState),
            new("ResizeFormDoesNotMoveCanvasControls", ConfigureResizeFormDoesNotMoveCanvasControls, AssertResizeFormDoesNotMoveCanvasControls),
            new("ResizePreviewWindowDoesNotChangeControlPositions", ConfigureResizePreviewWindowDoesNotChangeControlPositions, AssertResizePreviewWindowDoesNotChangeControlPositions),
            new("ButtonCornerRadiusPreviewExportMatch", ConfigureButtonCornerRadiusPreviewExportMatch, AssertButtonCornerRadiusPreviewExportMatch),
            new("OpacityZeroDesignerOnlyOutline", ConfigureOpacityZeroDesignerOnlyOutline, AssertOpacityZeroDesignerOnlyOutline),
            new("PreviewExportBoundsMatchAfterResize", ConfigurePreviewExportBoundsMatchAfterResize, AssertPreviewExportBoundsMatchAfterResize),
            new("GeneratedFormDesignSystemAppliesSemanticControlStates", ConfigureModernDesignSystemExport, AssertGeneratedFormDesignSystemAppliesSemanticControlStates),
            new("GeneratedFormDesignSystemStylesDataGridAndRuntimePreview", ConfigureModernDataGridDesignSystemExport, AssertGeneratedFormDesignSystemStylesDataGridAndRuntimePreview, RequiresRealDataGrid: true),
            new("RealDataGridExport", ConfigureRealDataGridExport, AssertRealDataGridExport, RequiresRealDataGrid: true),
            new("RealDataGridExportUsesValidColumnTags", ConfigureRealDataGridExport, AssertRealDataGridExportUsesValidColumnTags, RequiresRealDataGrid: true),
            new("DataGridExportGeneratesItemsSourceProperty", ConfigureRealDataGridExport, AssertDataGridExportGeneratesItemsSourceProperty, RequiresRealDataGrid: true),
            new("DataGridExportGeneratesRowDto", ConfigureRealDataGridExport, AssertDataGridExportGeneratesRowDto, RequiresRealDataGrid: true),
            new("DataGridExportGeneratesSampleData", ConfigureRealDataGridExportWithDemo, AssertDataGridExportGeneratesSampleData, RequiresRealDataGrid: true),
            new("DataGridExportBuildsWithoutManualFix", ConfigureRealDataGridExport, AssertDataGridExportBuildsWithoutManualFix, RequiresRealDataGrid: true),
            new("DataGridHeaderTemplateDoesNotGenerateEmptyBinding", ConfigureDataGridBindingSourceWorkflow, AssertDataGridHeaderTemplateDoesNotGenerateEmptyBinding, RequiresRealDataGrid: true),
            new("DataGridExportSettingsForSortResizeScroll", ConfigureRealDataGridExport, AssertDataGridExportSettingsForSortResizeScroll, RequiresRealDataGrid: true),
            new("DataGridCellPaddingDoesNotExceedRowHeight", ConfigureRealDataGridExport, AssertDataGridCellPaddingDoesNotExceedRowHeight, RequiresRealDataGrid: true),
            new("ExportedDataGridDefaultCellPaddingIsCompact", ConfigureRealDataGridExport, AssertExportedDataGridDefaultCellPaddingIsCompact, RequiresRealDataGrid: true),
            new("ExportedDataGridRowsTextNotClippedByStyle", ConfigureRealDataGridExport, AssertExportedDataGridRowsTextNotClippedByStyle, RequiresRealDataGrid: true),
            new("ExportedDataGridDoesNotRequireInterFont", ConfigureRealDataGridExport, AssertExportedDataGridDoesNotRequireInterFont, RequiresRealDataGrid: true),
            new("PreviewExportDataGridCellStyleMatch", ConfigureRealDataGridExport, AssertPreviewExportDataGridCellStyleMatch, RequiresRealDataGrid: true),
            new("AxamlPreviewDataGridUsesExportBinding", ConfigureRealDataGridExport, AssertAxamlPreviewDataGridUsesExportBinding, RequiresRealDataGrid: true),
            new("DataGridColumnWrapUsesTemplateColumn", ConfigureDataGridColumnWrapUsesTemplateColumn, AssertDataGridColumnWrapUsesTemplateColumn, RequiresRealDataGrid: true),
            new("DataGridColumnTrimmingExportedCorrectly", ConfigureDataGridColumnTrimmingExportedCorrectly, AssertDataGridColumnTrimmingExportedCorrectly, RequiresRealDataGrid: true),
            new("DataGridGroupingDoesNotGenerateInvalidBindings", ConfigureDataGridGroupingDoesNotGenerateInvalidBindings, AssertDataGridGroupingDoesNotGenerateInvalidBindings, RequiresRealDataGrid: true),
            new("DataGridManyColumnsShowsHorizontalScroll", ConfigureDataGridManyColumnsShowsHorizontalScroll, AssertDataGridManyColumnsShowsHorizontalScroll, RequiresRealDataGrid: true),
            new("DataGridPreviewExportSettingsMatch", ConfigureDataGridColumnWrapUsesTemplateColumn, AssertDataGridPreviewExportSettingsMatch, RequiresRealDataGrid: true),
            new("SqlDataGridExportGeneratesItemsSourceProperty", ConfigureSqlDataGridExport, AssertSqlDataGridExportGeneratesItemsSourceProperty, RequiresRealDataGrid: true),
            new("SqlDataGridExportGeneratesRowDto", ConfigureSqlDataGridExport, AssertSqlDataGridExportGeneratesRowDto, RequiresRealDataGrid: true),
            new("SqlDataGridWithConnectionAndQueryGeneratesSqlLoader", ConfigureSqlDataGridExportConnectionAllowed, AssertSqlDataGridWithConnectionAndQueryGeneratesSqlLoader, RequiresRealDataGrid: true),
            new("SqlDataGridConnectionStringExportedWhenAllowed", ConfigureSqlDataGridExportConnectionAllowed, AssertSqlDataGridConnectionStringExportedWhenAllowed, RequiresRealDataGrid: true),
            new("SqlDataGridConnectionStringNotExportedWhenDisabled", ConfigureSqlDataGridExport, AssertSqlDataGridConnectionStringNotExportedWhenDisabled, RequiresRealDataGrid: true),
            new("SqlDataGridQueryExported", ConfigureSqlDataGridExportConnectionAllowed, AssertSqlDataGridQueryExported, RequiresRealDataGrid: true),
            new("SqlDataGridGeneratesSqlClientPackageReference", ConfigureSqlDataGridExportConnectionAllowed, AssertSqlDataGridGeneratesSqlClientPackageReference, RequiresRealDataGrid: true),
            new("SqlDataGridColumnMappingUsesOriginalColumnNamesForReader", ConfigureSqlDataGridRussianColumnsExportAllowed, AssertSqlDataGridColumnMappingUsesOriginalColumnNamesForReader, RequiresRealDataGrid: true),
            new("SqlDataGridWithoutQueryDoesNotGenerateFakeSqlData", ConfigureUnconfiguredSqlDataGridNoData, AssertSqlDataGridWithoutQueryDoesNotGenerateFakeSqlData, RequiresRealDataGrid: true),
            new("DemoDataStillGeneratesSeedRowsOnlyInDemoMode", ConfigureUnconfiguredSqlDataGridDemoRows, AssertDemoDataStillGeneratesSeedRowsOnlyInDemoMode, RequiresRealDataGrid: true),
            new("SqlDataGridPreviewUsesRealSqlRowsWhenSourceConfigured", ConfigureSqlDataGridExport, AssertSqlDataGridPreviewUsesRealSqlRowsWhenSourceConfigured, RequiresRealDataGrid: true),
            new("SqlDataGridPreviewDoesNotUseDemoRowsForConfiguredSql", ConfigureSqlDataGridExport, AssertSqlDataGridPreviewDoesNotUseDemoRowsForConfiguredSql, RequiresRealDataGrid: true),
            new("SqlDataGridPreviewUsesDemoOnlyWhenExplicitlyEnabled", ConfigureUnconfiguredSqlDataGridDemoRows, AssertSqlDataGridPreviewUsesDemoOnlyWhenExplicitlyEnabled, RequiresRealDataGrid: true),
            new("SqlDataGridPreviewShowsNoDataWhenNoSourceAndDemoDisabled", ConfigureUnconfiguredSqlDataGridNoData, AssertSqlDataGridPreviewShowsNoDataWhenNoSourceAndDemoDisabled, RequiresRealDataGrid: true),
            new("SqlPreviewCacheInvalidatesWhenQueryChanges", ConfigureSqlDataGridExport, AssertSqlPreviewCacheInvalidatesWhenQueryChanges, RequiresRealDataGrid: true),
            new("SqlPreviewCacheKeyIncludesSourceKeyAndQueryHash", ConfigureSqlDataGridExport, AssertSqlPreviewCacheKeyIncludesSourceKeyAndQueryHash, RequiresRealDataGrid: true),
            new("SqlPreviewUsesDistinctProviderRowsWithoutSyntheticExpansion", ConfigureSqlDataGridExport, AssertSqlPreviewUsesDistinctProviderRowsWithoutSyntheticExpansion, RequiresRealDataGrid: true),
            new("SqlDataGridPreviewRuntimeRowsMatch", ConfigureSqlDataGridPreviewRuntimeRowsMatch, AssertSqlDataGridPreviewRuntimeRowsMatch, RequiresRealDataGrid: true),
            new("SqlDataGridWithoutConnectionDoesNotPretendRealData", ConfigureUnconfiguredSqlDataGridDemoRows, AssertSqlDataGridWithoutConnectionDoesNotPretendRealData, RequiresRealDataGrid: true),
            new("DemoDataPreviewExportsDemoRows", ConfigureUnconfiguredSqlDataGridDemoRows, AssertDemoDataPreviewExportsDemoRows, RequiresRealDataGrid: true),
            new("NoDataPreviewExportsEmptyCollectionWithWarning", ConfigureUnconfiguredSqlDataGridNoData, AssertNoDataPreviewExportsEmptyCollectionWithWarning, RequiresRealDataGrid: true),
            new("PreviewRuntimeDataModeMatches", ConfigureUnconfiguredSqlDataGridDemoRows, AssertPreviewRuntimeDataModeMatches, RequiresRealDataGrid: true),
            new("ExportedDataGridShowsRowsWhenDemoDataEnabled", ConfigureUnconfiguredSqlDataGridDemoRows, AssertExportedDataGridShowsRowsWhenDemoDataEnabled, RequiresRealDataGrid: true),
            new("ExportedDataGridShowsEmptyWithClearWarningWhenNoSourceAndNoDemo", ConfigureUnconfiguredSqlDataGridNoData, AssertExportedDataGridShowsEmptyWithClearWarningWhenNoSourceAndNoDemo, RequiresRealDataGrid: true),
            new("GeneratedDataGridAppAxamlIncludesDataGridTheme", ConfigureUnconfiguredSqlDataGridDemoRows, AssertGeneratedDataGridAppAxamlIncludesDataGridTheme, RequiresRealDataGrid: true),
            new("GeneratedDataGridRuntimeSelfCheckGenerated", ConfigureUnconfiguredSqlDataGridDemoRows, AssertGeneratedDataGridRuntimeSelfCheckGenerated, RequiresRealDataGrid: true),
            new("GeneratedDataGridWithFilterAndGroupPanelStillShowsRows", ConfigureUnconfiguredSqlDataGridDemoRows, AssertGeneratedDataGridWithFilterAndGroupPanelStillShowsRows, RequiresRealDataGrid: true),
            new("DataGridWithoutSourceExportsEmptyCollection", ConfigureUnconfiguredSqlDataGridNoData, AssertDataGridWithoutSourceExportsEmptyCollection, RequiresRealDataGrid: true),
            new("DataGridDemoRowsGeneratedOnlyWhenExplicitlyEnabled", ConfigureUnconfiguredSqlDataGridDemoRows, AssertDataGridDemoRowsGeneratedOnlyWhenExplicitlyEnabled, RequiresRealDataGrid: true),
            new("AxamlPreviewEmptyDataGridKeepsGroupPanel", ConfigureUnconfiguredSqlDataGridNoData, AssertAxamlPreviewEmptyDataGridKeepsGroupPanel, RequiresRealDataGrid: true),
            new("AxamlPreviewAndExportUseSameDataMode", ConfigureUnconfiguredSqlDataGridNoData, AssertAxamlPreviewAndExportUseSameDataMode, RequiresRealDataGrid: true),
            new("RuntimePreviewDoesNotDropGroupPanelHostRows", ConfigureUnconfiguredSqlDataGridNoData, AssertRuntimePreviewDoesNotDropGroupPanelHostRows, RequiresRealDataGrid: true),
            new("SqlDataGridDoesNotGenerateDemoSeed", ConfigureSqlDataGridExport, AssertSqlDataGridDoesNotGenerateDemoSeed, RequiresRealDataGrid: true),
            new("SqlDataGridAutoPromotesVisualExportToRuntimeRows", ConfigureSqlDataGridAutoPromotesVisualExportToRuntimeRows, AssertSqlDataGridAutoPromotesVisualExportToRuntimeRows, RequiresRealDataGrid: true),
            new("MultiFormSqlDataGridExportBuildsWithDistinctDtos", ConfigureMultiFormSqlDataGridExportBuildsWithDistinctDtos, AssertMultiFormSqlDataGridExportBuildsWithDistinctDtos, RequiresRealDataGrid: true),
            new("FiveFormSqlDataGridsExportBuilds", ConfigureFiveFormSqlDataGridsExportBuilds, AssertFiveFormSqlDataGridsExportBuilds, RequiresRealDataGrid: true),
            new("MultiFormSqlExportGeneratesConnectionForEveryForm", ConfigureMultiFormSqlExportWithGlobalAndDistinctSources, AssertMultiFormSqlExportGeneratesConnectionForEveryForm, RequiresRealDataGrid: true),
            new("MultiFormSqlExportGeneratesLoaderForEveryGrid", ConfigureMultiFormSqlExportWithGlobalAndDistinctSources, AssertMultiFormSqlExportGeneratesLoaderForEveryGrid, RequiresRealDataGrid: true),
            new("MultiFormSqlExportAssignsDataContextForEveryWindow", ConfigureMultiFormSqlExportWithGlobalAndDistinctSources, AssertMultiFormSqlExportAssignsDataContextForEveryWindow, RequiresRealDataGrid: true),
            new("MultiFormSqlExportLoadsRowsForSecondaryForms", ConfigureMultiFormSqlExportWithGlobalAndDistinctSources, AssertMultiFormSqlExportLoadsRowsForSecondaryForms, RequiresRealDataGrid: true),
            new("MultiFormSqlExportDoesNotDependOnActiveForm", ConfigureMultiFormSqlExportWithGlobalAndDistinctSources, AssertMultiFormSqlExportDoesNotDependOnActiveForm, RequiresRealDataGrid: true),
            new("MultiFormSqlExportSupportsDifferentSourcesPerForm", ConfigureMultiFormSqlExportWithGlobalAndDistinctSources, AssertMultiFormSqlExportSupportsDifferentSourcesPerForm, RequiresRealDataGrid: true),
            new("DllDataGridPreviewRuntimeRowsMatch", ConfigureDllTableDataGridExport, AssertDllDataGridPreviewRuntimeRowsMatch, RequiresRealDataGrid: true),
            new("ManualDataGridPreviewRuntimeRowsMatch", ConfigureManualColumnsViaColumnEditor, AssertManualDataGridPreviewRuntimeRowsMatch, RequiresRealDataGrid: true),
            new("DllTableDataGridExportGeneratesUnderstandableBinding", ConfigureDllTableDataGridExport, AssertDllTableDataGridExportGeneratesUnderstandableBinding, RequiresRealDataGrid: true),
            new("DataGridExportDoesNotGenerateEmptyBindings", ConfigureSqlDataGridExport, AssertDataGridExportDoesNotGenerateEmptyBindings, RequiresRealDataGrid: true),
            new("DataGridExportDoesNotGenerateInvalidXDataType", ConfigureSqlDataGridExport, AssertDataGridExportDoesNotGenerateInvalidXDataType, RequiresRealDataGrid: true),
            new("DataGridExportDoesNotRequireManualAxamlFix", ConfigureDllTableDataGridExport, AssertDataGridExportDoesNotRequireManualAxamlFix, RequiresRealDataGrid: true),
            new("DataGridExportSupportsManualColumns", ConfigureManualColumnsViaColumnEditor, AssertDataGridExportSupportsManualColumns, RequiresRealDataGrid: true),
            new("DataGridExportSupportsSqlSchemaAsDto", ConfigureSqlDataGridExport, AssertDataGridExportSupportsSqlSchemaAsDto, RequiresRealDataGrid: true),
            new("DataGridExportSupportsDllSchemaAsDtoOrReference", ConfigureDllTableDataGridExport, AssertDataGridExportSupportsDllSchemaAsDtoOrReference, RequiresRealDataGrid: true),
            new("ColumnEditorApplyUpdatesDataGridModel", ConfigureColumnEditorApplyUpdatesDataGridModel, AssertColumnEditorApplyUpdatesDataGridModel, RequiresRealDataGrid: true),
            new("ColumnEditorCancelDoesNotModifyModel", ConfigureColumnEditorCancelDoesNotModifyModel, AssertColumnEditorCancelDoesNotModifyModel, RequiresRealDataGrid: true),
            new("ColumnEditorAddRemoveDuplicateWorks", ConfigureColumnEditorAddRemoveDuplicateWorks, AssertColumnEditorAddRemoveDuplicateWorks, RequiresRealDataGrid: true),
            new("ColumnEditorReorderColumnsAffectsPreviewAndExport", ConfigureColumnEditorReorderColumnsAffectsPreviewAndExport, AssertColumnEditorReorderColumnsAffectsPreviewAndExport, RequiresRealDataGrid: true),
            new("ColumnEditorDoesNotCallApplyDocument", ConfigureColumnEditorDoesNotCallApplyDocument, AssertColumnEditorDoesNotCallApplyDocument, RequiresRealDataGrid: true),
            new("ColumnEditorDoesNotResetSelectedControl", ConfigureColumnEditorDoesNotResetSelectedControl, AssertColumnEditorDoesNotResetSelectedControl, RequiresRealDataGrid: true),
            new("DataModeShowsSelectedDataGridSourceOnly", ConfigureDataModeShowsSelectedDataGridSourceOnly, AssertDataModeShowsSelectedDataGridSourceOnly, RequiresRealDataGrid: true),
            new("DataModeDoesNotEditColumnsDirectly", ConfigureSimpleFormExport, AssertDataModeDoesNotEditColumnsDirectly),
            new("DataModeOpensColumnEditorForSelectedGrid", ConfigureDataModeShowsSelectedDataGridSourceOnly, AssertDataModeOpensColumnEditorForSelectedGrid, RequiresRealDataGrid: true),
            new("DataModeHandlesSameGridNamesAcrossForms", ConfigureDataModeHandlesSameGridNamesAcrossForms, AssertDataModeHandlesSameGridNamesAcrossForms, RequiresRealDataGrid: true),
            new("DllImportDetectsLinqToSqlTableAttribute", ConfigureLinqToSqlDllImport, AssertDllImportDetectsLinqToSqlTableAttribute),
            new("DllImportDetectsLinqToSqlColumnAttributes", ConfigureLinqToSqlDllImport, AssertDllImportDetectsLinqToSqlColumnAttributes),
            new("DllImportSupportsDuplicateTableNamesAcrossDlls", ConfigureDuplicateTableDllImport, AssertDllImportSupportsDuplicateTableNamesAcrossDlls),
            new("DllImportBuildsStableSourceKey", ConfigureLinqToSqlDllImport, AssertDllImportBuildsStableSourceKey),
            new("DllSearchFindsTypeTableAndColumn", ConfigureLinqToSqlDllImport, AssertDllSearchFindsTypeTableAndColumn),
            new("LoadedDllPanelShowsCounts", ConfigureLinqToSqlDllImport, AssertLoadedDllPanelShowsCounts),
            new("RemoveDllRemovesMetadataAndSources", ConfigureLinqToSqlDllImport, AssertRemoveDllRemovesMetadataAndSources),
            new("RemoveDllDetachesDataGridSourceWithoutCrash", ConfigureLinqToSqlDllImport, AssertRemoveDllDetachesDataGridSourceWithoutCrash),
            new("FailedDllCanBeRemoved", ConfigureFailedDllImport, AssertFailedDllCanBeRemoved),
            new("DllLoadFailureShowsErrorInUi", ConfigureFailedDllImport, AssertDllLoadFailureShowsErrorInUi),
            new("DuplicateTableNamesDisplayDllName", ConfigureDuplicateTableDllImport, AssertDuplicateTableNamesDisplayDllName),
            new("SearchManyTablesIsDebouncedAndFast", ConfigureManyTableDllImport, AssertSearchManyTablesIsDebouncedAndFast),
            new("ImportManyDllsDoesNotLoadPreviewRowsAutomatically", ConfigureManyTableDllImport, AssertImportManyDllsDoesNotLoadPreviewRowsAutomatically),
            new("RemoveDllReleasesMetadataAndCaches", ConfigureManyTableDllImport, AssertRemoveDllReleasesMetadataAndCaches),
            new("DataGridBindingUsesFullDllSourceKey", ConfigureLinqToSqlDllImport, AssertDataGridBindingUsesFullDllSourceKey),
            new("DllTablePreviewDoesNotUseFakeRowsWhenRealRowsAvailable", ConfigureDllTablePreviewRealRows, AssertDllTablePreviewDoesNotUseFakeRowsWhenRealRowsAvailable),
            new("DllTablePreviewClearlyMarksSampleRows", ConfigureLinqToSqlDllImport, AssertDllTablePreviewClearlyMarksSampleRows),
            new("DllTablePreviewTopNWorks", ConfigureDllTablePreviewTopN, AssertDllTablePreviewTopNWorks),
            new("DllTablePreviewSortThenTopNWorks", ConfigureDllTablePreviewSortThenTopN, AssertDllTablePreviewSortThenTopNWorks),
            new("GeneratedNugetConfigContainsAllowInsecureConnectionsForHttpSource", ConfigureSimpleFormExport, AssertGeneratedNugetConfigContainsAllowInsecureConnectionsForHttpSource),
            new("ExportGeneratesNugetConfigInProjectRoot", ConfigureSimpleFormExport, AssertExportGeneratesNugetConfigInProjectRoot),
            new("ExportedNugetConfigContainsNugetOrgByDefault", ConfigureSimpleFormExport, AssertExportedNugetConfigContainsNugetOrgByDefault),
            new("ExportedNugetConfigContainsCustomSource", ConfigureSimpleFormExport, AssertExportedNugetConfigContainsCustomSource),
            new("ExportedNugetConfigSupportsHttpAllowInsecure", ConfigureSimpleFormExport, AssertExportedNugetConfigSupportsHttpAllowInsecure),
            new("DotnetRestoreWorksFromExportedProjectRoot", ConfigureRealDataGridExport, AssertDotnetRestoreWorksFromExportedProjectRoot, RequiresRealDataGrid: true),
            new("ValidateBuildUsesSameNugetConfigAsExportedProject", ConfigureSimpleFormExport, AssertValidateBuildUsesSameNugetConfigAsExportedProject),
            new("BuildOutputDeduplicatesRepeatedNet6Warning", ConfigureSimpleFormExport, AssertBuildOutputDeduplicatesRepeatedNet6Warning),
            new("ArtifactsCleanupDeletesOldExportRuns", ConfigureSimpleFormExport, AssertArtifactsCleanupDeletesOldExportRuns),
            new("ExportGeneratesAtMostOneReadme", ConfigureRealDataGridExport, AssertExportGeneratesAtMostOneReadme, RequiresRealDataGrid: true),
            new("GeneratedSolutionHasConsistentAvaloniaVersions", ConfigureRealDataGridExport, AssertGeneratedSolutionHasConsistentAvaloniaVersions, RequiresRealDataGrid: true),
            new("GeneratedSolutionDoesNotIncludeDesignerOnlyPackages", ConfigureRealDataGridExport, AssertGeneratedSolutionDoesNotIncludeDesignerOnlyPackages, RequiresRealDataGrid: true),
            new("LogicTemplateTypingDoesNotCallApplyDocument", ConfigureLogicTemplateEditor, AssertLogicTemplateTypingDoesNotCallApplyDocument, RequiresRealDataGrid: true),
            new("LogicTemplateTypingDoesNotRebuildPropertyGrid", ConfigureLogicTemplateEditor, AssertLogicTemplateTypingDoesNotRebuildPropertyGrid, RequiresRealDataGrid: true),
            new("LogicTemplateApplyUpdatesModel", ConfigureLogicTemplateEditor, AssertLogicTemplateApplyUpdatesModel, RequiresRealDataGrid: true),
            new("LogicTemplateCancelDoesNotModifyModel", ConfigureLogicTemplateEditor, AssertLogicTemplateCancelDoesNotModifyModel, RequiresRealDataGrid: true),
            new("ExportDataGridSelectionToTextBoxBuilds", ConfigureDataGridSelectionToTextBoxExport, AssertExportDataGridSelectionToTextBoxBuilds, RequiresRealDataGrid: true),
            new("ExportDataGridSelectionToTextBlockBuilds", ConfigureDataGridSelectionToTextBlockExport, AssertExportDataGridSelectionToTextBlockBuilds, RequiresRealDataGrid: true),
            new("ExportLogicInvalidPlaceholderShowsWarning", ConfigureDataGridSelectionInvalidPlaceholder, AssertExportLogicInvalidPlaceholderShowsWarning, RequiresRealDataGrid: true),
            new("ExportLogicUsesGeneratedRowDtoProperties", ConfigureDataGridSelectionToTextBoxExport, AssertExportLogicUsesGeneratedRowDtoProperties, RequiresRealDataGrid: true),
            new("InteractionsExport", ConfigureInteractionsExport, AssertInteractionsExport, RequiresRealDataGrid: true),
            new("MultiFormOpenFormExport", ConfigureMultiFormOpenFormExport, AssertMultiFormOpenFormExport),
            new("MultiFormDocumentStateIsolation", ConfigureMultiFormDocumentStateIsolation, AssertMultiFormDocumentStateIsolation),
            new("DesignerDocumentSessionCreatedForOpenedForm", ConfigureDesignerDocumentSessionCreatedForOpenedForm, AssertDesignerDocumentSessionCreatedForOpenedForm),
            new("SelectionLivesInActiveDocumentSession", ConfigureSelectionLivesInActiveDocumentSession, AssertSelectionLivesInActiveDocumentSession),
            new("SwitchingFormsSwitchesDocumentSession", ConfigureSwitchingFormsSwitchesDocumentSession, AssertSwitchingFormsSwitchesDocumentSession),
            new("AddFormDoesNotMutateExistingSession", ConfigureAddFormDoesNotMutateExistingSession, AssertAddFormDoesNotMutateExistingSession),
            new("DeletingFormDisposesItsSession", ConfigureDeletingFormDisposesItsSession, AssertDeletingFormDisposesItsSession),
            new("NewProjectDisposesAllOldDocumentSessions", ConfigureNewProjectDisposesAllOldDocumentSessions, AssertNewProjectDisposesAllOldDocumentSessions),
            new("PropertyInspectorFollowsActiveSessionSelection", ConfigurePropertyInspectorFollowsActiveSessionSelection, AssertPropertyInspectorFollowsActiveSessionSelection),
            new("UndoRedoIsIsolatedPerDocumentSession", ConfigureUndoRedoIsIsolatedPerDocumentSession, AssertUndoRedoIsIsolatedPerDocumentSession),
            new("ApplyDocumentPreservesSelectionThroughSession", ConfigureApplyDocumentPreservesSelectionThroughSession, AssertApplyDocumentPreservesSelectionThroughSession),
            new("DesignerSurfaceCanBeCreatedWithoutMainWindow", ConfigureSimpleFormExport, AssertDesignerSurfaceCanBeCreatedWithoutMainWindow),
            new("DesignerSurfaceCanAttachDocumentSession", ConfigureSimpleFormExport, AssertDesignerSurfaceCanAttachDocumentSession),
            new("DesignerSurfaceCanSwitchDocumentSessions", ConfigureSimpleFormExport, AssertDesignerSurfaceCanSwitchDocumentSessions),
            new("DesignerSurfaceRendersStandardControl", ConfigureDesignerSurfaceStandardControl, AssertDesignerSurfaceRendersStandardControl),
            new("DesignerSurfaceToolboxUsesSharedRegistry", ConfigureEremexTextEditorVerticalSlice, AssertDesignerSurfaceToolboxUsesSharedRegistry),
            new("DesignerSurfaceSelectionUpdatesSession", ConfigureDesignerSurfaceStandardControl, AssertDesignerSurfaceSelectionUpdatesSession),
            new("SessionSelectionUpdatesDesignerSurface", ConfigureDesignerSurfaceStandardControl, AssertSessionSelectionUpdatesDesignerSurface),
            new("DesignerSurfaceInspectorUsesSessionSelection", ConfigureDesignerSurfaceStandardControl, AssertDesignerSurfaceInspectorUsesSessionSelection),
            new("DesignerPropertyInspectorFavoritesRoundTrip", ConfigureDesignerSurfaceStandardControl, AssertDesignerPropertyInspectorFavoritesRoundTrip),
            new("DesignerSurfaceWorksWithFakeHostServices", ConfigureDesignerSurfaceStandardControl, AssertDesignerSurfaceWorksWithFakeHostServices),
            new("DesignerHostServicesRouteClipboardFilePickerDialogAndNotification", ConfigureSimpleFormExport, AssertDesignerHostServicesRouteClipboardFilePickerDialogAndNotification),
            new("DesignerHostArchitectureGuard", ConfigureSimpleFormExport, AssertDesignerHostArchitectureGuard),
            new("MainWindowSwitchFormRebindsSameDesignerSurface", ConfigureDesignerSurfaceStandardControl, AssertMainWindowSwitchFormRebindsSameDesignerSurface),
            new("DesignerSurfaceDoesNotRequireMainWindowConcreteType", ConfigureSimpleFormExport, AssertDesignerSurfaceDoesNotRequireMainWindowConcreteType),
            new("DesignerSurfaceRendersEremexTextEditor", ConfigureEremexTextEditorVerticalSlice, AssertDesignerSurfaceRendersEremexTextEditor),
            new("DesignerSurfaceRendersEremexDataGrid", ConfigureEremexDataGridControlVerticalSlice, AssertDesignerSurfaceRendersEremexDataGrid),
            new("AxamlImportCanvasButtonTextBox", ConfigureSimpleFormExport, AssertAxamlImportCanvasButtonTextBox),
            new("AxamlRoundTripUpdatesButtonProperties", ConfigureSimpleFormExport, AssertAxamlRoundTripUpdatesButtonProperties),
            new("AxamlRoundTripPreservesComment", ConfigureSimpleFormExport, AssertAxamlRoundTripPreservesComment),
            new("AxamlRoundTripPreservesUnknownAttribute", ConfigureSimpleFormExport, AssertAxamlRoundTripPreservesUnknownAttribute),
            new("AxamlRoundTripPreservesUnknownControl", ConfigureSimpleFormExport, AssertAxamlRoundTripPreservesUnknownControl),
            new("AxamlRoundTripPreservesRawContent", ConfigureSimpleFormExport, AssertAxamlRoundTripPreservesRawContent),
            new("AxamlRoundTripCanInsertNewButtonIntoCanvas", ConfigureSimpleFormExport, AssertAxamlRoundTripCanInsertNewButtonIntoCanvas),
            new("AxamlRoundTripDeletesOnlyOwnedElement", ConfigureSimpleFormExport, AssertAxamlRoundTripDeletesOnlyOwnedElement),
            new("AxamlRoundTripDoesNotReformatWholeDocument", ConfigureSimpleFormExport, AssertAxamlRoundTripDoesNotReformatWholeDocument),
            new("AxamlRoundTripDetectsExternalChange", ConfigureSimpleFormExport, AssertAxamlRoundTripDetectsExternalChange),
            new("VsHostProtocolHandshakeWorks", ConfigureSimpleFormExport, AssertVsHostProtocolHandshakeWorks),
            new("VsHostCanStartAndAcceptConnection", ConfigureSimpleFormExport, AssertVsHostCanStartAndAcceptConnection),
            new("VsHostUsesSharedDesignerSurface", ConfigureSimpleFormExport, AssertVsHostUsesSharedDesignerSurface),
            new("VsixBridgeDoesNotReferenceAvaloniaVisualAssemblies", ConfigureSimpleFormExport, AssertVsixBridgeDoesNotReferenceAvaloniaVisualAssemblies),
            new("VsixCommandTableRegistersVisibleToolsCommand", ConfigureSimpleFormExport, AssertVsixCommandTableRegistersVisibleToolsCommand),
            new("VsixPackageRuntimeDependencyClosureIsValid", ConfigureSimpleFormExport, AssertVsixPackageRuntimeDependencyClosureIsValid),
            new("VsHostReturnsMinimalAxamlPatch", ConfigureSimpleFormExport, AssertVsHostReturnsMinimalAxamlPatch),
            new("VsHostRoundTripPreservesComment", ConfigureSimpleFormExport, AssertVsHostRoundTripPreservesComment),
            new("VsHostRoundTripPreservesUnknownAttribute", ConfigureSimpleFormExport, AssertVsHostRoundTripPreservesUnknownAttribute),
            new("VsHostRejectsPatchForStaleDocumentVersion", ConfigureSimpleFormExport, AssertVsHostRejectsPatchForStaleDocumentVersion),
            new("BridgeSurvivesVsHostDisconnect", ConfigureSimpleFormExport, AssertBridgeSurvivesVsHostDisconnect),
            new("MultiFormToolboxDropPropertyEdit", ConfigureMultiFormToolboxDropPropertyEdit, AssertMultiFormToolboxDropPropertyEdit, RequiresRealDataGrid: true),
            new("MultiFormSameControlNamesPropertyGridEdit", ConfigureMultiFormSameControlNamesPropertyGridEdit, AssertMultiFormSameControlNamesPropertyGridEdit, RequiresRealDataGrid: true),
            new("AddFormSimpleIsolation", ConfigureAddFormSimpleIsolation, AssertAddFormSimpleIsolation),
            new("SwitchFormsClearsInspector", ConfigureSwitchFormsClearsInspector, AssertSwitchFormsClearsInspector),
            new("PropertyGridTextEditUsesLocalBuffer", ConfigurePropertyGridTextEditUsesLocalBuffer, AssertPropertyGridTextEditUsesLocalBuffer),
            new("EditAfterAddFormWorks", ConfigureEditAfterAddFormWorks, AssertEditAfterAddFormWorks),
            new("EditTextAfterAddFormStillWorks", ConfigureEditTextAfterAddFormStillWorks, AssertEditTextAfterAddFormStillWorks),
            new("ChangeForegroundAfterAddFormWorks", ConfigureChangeForegroundAfterAddFormWorks, AssertChangeForegroundAfterAddFormWorks),
            new("ChangeBackgroundAfterAddFormWorks", ConfigureChangeBackgroundAfterAddFormWorks, AssertChangeBackgroundAfterAddFormWorks),
            new("ForegroundPersistsInModel", ConfigureForegroundPersistsInModel, AssertForegroundPersistsInModel),
            new("ForegroundAppearsInExportedXaml", ConfigureForegroundAppearsInExportedXaml, AssertForegroundAppearsInExportedXaml),
            new("SameSelectionDoesNotRebuildPropertyGrid", ConfigureSameSelectionDoesNotRebuildPropertyGrid, AssertSameSelectionDoesNotRebuildPropertyGrid),
            new("PropertiesPanelVisibleAfterAddFormAndSelectControl", ConfigurePropertiesPanelVisibleAfterAddFormAndSelectControl, AssertPropertiesPanelVisibleAfterAddFormAndSelectControl),
            new("InspectorShowsActiveFormPropertiesAfterSwitch", ConfigureInspectorShowsActiveFormPropertiesAfterSwitch, AssertInspectorShowsActiveFormPropertiesAfterSwitch),
            new("RenameFormDoesNotDuplicatePropertyRows", ConfigureRenameFormDoesNotDuplicatePropertyRows, AssertRenameFormDoesNotDuplicatePropertyRows),
            new("WidthHeightEditDoesNotLockInspector", ConfigureWidthHeightEditDoesNotLockInspector, AssertWidthHeightEditDoesNotLockInspector),
            new("EmptyButtonTextIsPreserved", ConfigureEmptyButtonTextIsPreserved, AssertEmptyButtonTextIsPreserved),
            new("PropertyEditBlockedForStaleInspectorTarget", ConfigurePropertyEditBlockedForStaleInspectorTarget, AssertPropertyEditBlockedForStaleInspectorTarget),
            new("AddFormRegressionStillWorks", ConfigureAddFormRegressionStillWorks, AssertAddFormRegressionStillWorks),
            new("NewProjectClearsPreviousFormsAndState", ConfigureNewProjectClearsPreviousFormsAndState, AssertNewProjectClearsPreviousFormsAndState),
            new("NewProjectAfterSavedMultiFormProjectClearsAllOldForms", ConfigureNewProjectAfterSavedMultiFormProjectClearsAllOldForms, AssertNewProjectAfterSavedMultiFormProjectClearsAllOldForms),
            new("NewProjectDoesNotReuseOldFormNamesOrIds", ConfigureNewProjectDoesNotReuseOldFormNamesOrIds, AssertNewProjectDoesNotReuseOldFormNamesOrIds),
            new("NewProjectReleasesOldFormsAndControls", ConfigureNewProjectReleasesOldFormsAndControls, AssertNewProjectReleasesOldFormsAndControls),
            new("NewProjectClearsDataDllExportCaches", ConfigureNewProjectClearsDataDllExportCaches, AssertNewProjectClearsDataDllExportCaches),
            new("PropertyEditDoesNotTriggerExportPipeline", ConfigurePropertyEditDoesNotTriggerExportPipeline, AssertPropertyEditDoesNotTriggerExportPipeline),
            new("ExportDoesNotMutateEditorState", ConfigureExportDoesNotMutateEditorState, AssertExportDoesNotMutateEditorState),
            new("BuildSecondaryFormsDoesNotCallApplyDocument", ConfigureBuildSecondaryFormsDoesNotCallApplyDocument, AssertBuildSecondaryFormsDoesNotCallApplyDocument),
            new("ExportWhileEditingPropertyDoesNotResetTextBox", ConfigureExportWhileEditingPropertyDoesNotResetTextBox, AssertExportWhileEditingPropertyDoesNotResetTextBox),
            new("ValidateBuildDoesNotChangeActiveForm", ConfigureValidateBuildDoesNotChangeActiveForm, AssertValidateBuildDoesNotChangeActiveForm),
            new("ExportAfterNewProjectDoesNotUseOldDocuments", ConfigureExportAfterNewProjectDoesNotUseOldDocuments, AssertExportAfterNewProjectDoesNotUseOldDocuments),
            new("ResizeDoesNotRebuildPropertyGridOnEveryMove", ConfigureResizeDoesNotRebuildPropertyGridOnEveryMove, AssertResizeDoesNotRebuildPropertyGridOnEveryMove),
            new("DragAfterAddFormWorks", ConfigureDragAfterAddFormWorks, AssertDragAfterAddFormWorks),
            new("AddEmptySecondFormDoesNotBreakFirstFormPropertyGrid", ConfigureAddEmptySecondFormDoesNotBreakFirstFormPropertyGrid, AssertAddEmptySecondFormDoesNotBreakFirstFormPropertyGrid, RequiresRealDataGrid: true),
            new("AddEmptySecondFormDoesNotBreakFirstFormInspectorAndLogic", ConfigureAddEmptySecondFormDoesNotBreakFirstFormInspectorAndLogic, AssertAddEmptySecondFormDoesNotBreakFirstFormInspectorAndLogic, RequiresRealDataGrid: true),
            new("AlphaEndToEndProjectExport", ConfigureAlphaEndToEndProjectExport, AssertAlphaEndToEndProjectExport, RequiresRealDataGrid: true),
            new("DataGridBindingSourceWorkflow", ConfigureDataGridBindingSourceWorkflow, AssertDataGridBindingSourceWorkflow, RequiresRealDataGrid: true),
            new("OpenFormPreviewAndExport", ConfigureOpenFormPreviewAndExport, AssertOpenFormPreviewAndExport),
            new("PropertyGridDataGridSetup", ConfigurePropertyGridDataGridSetup, AssertPropertyGridDataGridSetup, RequiresRealDataGrid: true),
            new("SaveLoadMultiFormProject", ConfigureSaveLoadMultiFormProject, AssertSaveLoadMultiFormProject, RequiresRealDataGrid: true),
            new("ExportToProjectBuildValidation", ConfigureExportToProjectBuildValidation, AssertExportToProjectBuildValidation, RequiresRealDataGrid: true),
            new("PluginFallbackExport", ConfigurePluginFallbackExport, AssertPluginFallbackExport),
            new("EremexThemeDoesNotChangeDesignerChrome", ConfigureEremexTextEditorVerticalSlice, AssertEremexThemeDoesNotChangeDesignerChrome),
            new("EremexTextEditorVerticalSlice", ConfigureEremexTextEditorVerticalSlice, AssertEremexTextEditorVerticalSlice),
            new("EremexDataGridControlVerticalSlice", ConfigureEremexDataGridControlVerticalSlice, AssertEremexDataGridControlVerticalSlice),
            new("GridLayoutExport", ConfigureGridLayoutExport, AssertGridLayoutExport),
            new("StackPanelLayoutExport", ConfigureStackPanelLayoutExport, AssertStackPanelLayoutExport),
            new("LayoutContainerExport", ConfigureLayoutContainerExport, AssertLayoutContainerExport),
            new("LayoutConversionExport", ConfigureLayoutConversionExport, AssertLayoutConversionExport),
            new("ResponsiveLayoutExport_StackPanel", ConfigureResponsiveStackPanelExport, AssertResponsiveStackPanelExport),
            new("ResponsiveLayoutExport_CanvasFallback", ConfigureResponsiveCanvasFallbackExport, AssertResponsiveCanvasFallbackExport),
            new("RuntimePreviewCanLoadSimpleWindow", ConfigureSimpleFormExport, AssertRuntimePreviewCanLoadSimpleWindow),
            new("RuntimePreviewCanLoadDataGrid", ConfigureRealDataGridExport, AssertRuntimePreviewCanLoadDataGrid, RequiresRealDataGrid: true)
        };

        var scenarioFilter = Environment.GetEnvironmentVariable("SMOKE_SCENARIO_FILTER");
        if (!string.IsNullOrWhiteSpace(scenarioFilter))
        {
            scenarios = scenarios
                .Where(scenario => scenario.Name.Contains(scenarioFilter, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (scenarios.Length == 0)
                throw new InvalidOperationException($"No smoke scenarios matched SMOKE_SCENARIO_FILTER='{scenarioFilter}'.");

            Console.WriteLine($"Scenario filter: {scenarioFilter} ({scenarios.Length} matched)");
        }

        var failed = 0;
        foreach (var scenario in scenarios)
        {
            try
            {
                RunScenario(runRoot, scenario);
                Console.WriteLine($"PASS {scenario.Name}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"FAIL {scenario.Name}: {ex}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? $"Smoke tests passed: {scenarios.Length}/{scenarios.Length}"
            : $"Smoke tests failed: {failed}/{scenarios.Length}");

        PruneSmokeRuns(artifactsRoot, runRoot);
        return failed == 0 ? 0 : 1;
    }

    private static void RunScenario(string artifactsRoot, SmokeScenario scenario)
    {
        var projectPath = Path.Combine(artifactsRoot, scenario.Name);
        Directory.CreateDirectory(projectPath);

        var viewModel = CreateViewModel(scenario.Name);
        scenario.Configure(viewModel);
        viewModel.RefreshDiagnostics();
        viewModel.GenerateXaml();

        var context = new SmokeContext(
            Scenario: scenario,
            ViewModel: viewModel,
            ProjectPath: projectPath,
            Xaml: viewModel.GeneratedXaml,
            CSharp: viewModel.GeneratedCSharp,
            GeneratedFiles: viewModel.GeneratedFiles.ToList(),
            ChecklistText: string.Join(Environment.NewLine, viewModel.ExportChecklistItems.Select(item => $"{item.Title}: {item.Value} {item.Details}")),
            DiagnosticsText: string.Join(Environment.NewLine, viewModel.Diagnostics.Select(item => $"{item.Category}: {item.Message} {item.Recommendation}")));

        WriteAvaloniaProject(context);
        scenario.Assert(context);
        DotnetBuild(projectPath);
    }

    private static MainWindowViewModel CreateViewModel(string scenarioName)
    {
        var registry = new DesignerRegistry();
        BuiltInControlRegistrar.Register(registry);
        registry.RegisterBindingProvider(new ReflectionBindingMetadataProvider());

        var viewModel = new MainWindowViewModel(registry)
        {
            ExportProjectNamespace = $"SmokeGenerated.{scenarioName.Replace("-", "").Replace("_", "")}",
            ExportTarget = MainWindowViewModel.ExportTargetMainWindow,
            XamlVerbosity = MainWindowViewModel.XamlVerbosityCompact,
            LayoutExportMode = MainWindowViewModel.LayoutExportModeCanvas,
            DataGridExportMode = MainWindowViewModel.DataGridExportModeVisual,
            IncludeExportComments = true,
            IncludeSampleData = false,
            IncludeCrudSkeleton = false,
            IncludeCommunityToolkitAttributes = false,
            IncludePluginRuntimeReferences = false,
            DesignWidth = 900,
            DesignHeight = 620,
            FormTitle = scenarioName
        };

        viewModel.Controls.Clear();
        viewModel.BindingSources.Clear();
        viewModel.Interactions.Clear();
        return viewModel;
    }

    private static void ConfigureSimpleFormExport(MainWindowViewModel vm)
    {
        vm.Controls.Add(Control(DesignerControlTypes.TextBlock, "TitleText", 36, 30, 360, 32, text: "Simple export smoke"));
        vm.Controls.Add(Control(DesignerControlTypes.TextBox, "NameTextBox", 36, 90, 260, 40, placeholder: "Enter name"));
        vm.Controls.Add(Control(DesignerControlTypes.CheckBox, "EnabledCheckBox", 36, 148, 180, 32, text: "Enabled"));
        vm.Controls.Add(Control(DesignerControlTypes.Border, "ContentCard", 340, 86, 260, 110, background: "#F8FAFC", border: "#CBD5E1", radius: 12));
        vm.Controls.Add(Control(DesignerControlTypes.Button, "SaveButton", 36, 220, 150, 42, text: "Save", background: "#2563EB", foreground: "#FFFFFF", border: "#1D4ED8", radius: 10));
    }

    private static void ConfigureModernDesignSystemExport(MainWindowViewModel vm)
    {
        ConfigureSimpleFormExport(vm);
        vm.XamlVerbosity = MainWindowViewModel.XamlVerbosityFullStyled;
    }

    private static void ConfigureModernDataGridDesignSystemExport(MainWindowViewModel vm)
    {
        ConfigureRealDataGridExport(vm);
        vm.XamlVerbosity = MainWindowViewModel.XamlVerbosityFullStyled;
    }

    private static void AssertLocalizationKeysExistForMainUi(SmokeContext context)
    {
        var text = UiText.Current;
        if (text.Language != UiLanguage.Russian)
            throw new InvalidOperationException("Default UI language must be Russian.");
        if (text.KeyCount < 70)
            throw new InvalidOperationException($"Localization catalog is unexpectedly small: {text.KeyCount}.");

        RequireEqual("Новый проект", text.NewProject, "New Project must be localized.");
        RequireEqual("Открыть проект", text.OpenProject, "Open Project must be localized.");
        RequireEqual("Сохранить", text.Save, "Save must be localized.");
        RequireEqual("Настройки", text.Settings, "Settings must be localized.");
        RequireEqual("Проверить Build", text.ValidateBuild, "Validate Build must keep Build technical term.");
    }

    private static void AssertPropertyNamesAreNotLocalized(SmokeContext context)
    {
        RequireTechnicalName(UiText.TechnicalPropertyNames, "Name");
        RequireTechnicalName(UiText.TechnicalPropertyNames, "Text");
        RequireTechnicalName(UiText.TechnicalPropertyNames, "Width");
        RequireTechnicalName(UiText.TechnicalPropertyNames, "Height");
        RequireTechnicalName(UiText.TechnicalPropertyNames, "Background");
        RequireTechnicalName(UiText.TechnicalPropertyNames, "Foreground");

        if (UiText.TechnicalPropertyNames.Contains("Ширина") || UiText.TechnicalPropertyNames.Contains("Имя"))
            throw new InvalidOperationException("Technical property names must not be localized.");
    }

    private static void AssertControlNamesAreNotLocalized(SmokeContext context)
    {
        RequireTechnicalName(UiText.TechnicalControlNames, "Button");
        RequireTechnicalName(UiText.TechnicalControlNames, "TextBox");
        RequireTechnicalName(UiText.TechnicalControlNames, "TextBlock");
        RequireTechnicalName(UiText.TechnicalControlNames, "DataGrid");
        RequireTechnicalName(UiText.TechnicalControlNames, "Border");

        if (UiText.TechnicalControlNames.Contains("Кнопка") || UiText.TechnicalControlNames.Contains("Текстовое поле"))
            throw new InvalidOperationException("Technical control names must not be localized.");
    }

    private static void AssertRussianUiTextAppliedToMainButtons(SmokeContext context)
    {
        var vm = context.ViewModel;
        RequireEqual(UiText.Current.NewProject, vm.NewEditorCommand?.Title ?? "", "New command title should use UiText.");
        RequireEqual(UiText.Current.OpenProject, vm.OpenEditorCommand?.Title ?? "", "Open command title should use UiText.");
        RequireEqual(UiText.Current.Save, vm.SaveEditorCommand?.Title ?? "", "Save command title should use UiText.");
        RequireEqual(UiText.Current.Preview, vm.PreviewEditorCommand?.Title ?? "", "Preview command title should use UiText.");
        if (!((vm.OpenExportPipelineEditorCommand?.Title ?? "").Contains("Export", StringComparison.Ordinal)))
            throw new InvalidOperationException("Export command title should keep the Export technical term.");
        RequireEqual(UiText.Current.Settings, vm.Texts.Settings, "MainWindowViewModel should expose UiText catalog.");
    }

    private static void AssertMissingLocalizationFallsBackToEnglishOrKey(SmokeContext context)
    {
        const string missingKey = "MissingLocalizationSmokeKey";
        RequireEqual(missingKey, UiText.Current.Get(missingKey), "Missing localization should fall back to the key.");
    }

    private static void AssertPropertyDescriptionProviderHasRussianDescriptions(SmokeContext context)
    {
        RequireContains(PropertyDescriptionProvider.GetDescription("Name"), "Уникальное имя", "Name should have a Russian property description.");
        RequireContains(PropertyDescriptionProvider.GetDescription("Width"), "Ширина", "Width should have a Russian property description.");
        RequireContains(PropertyDescriptionProvider.GetDescription("Height"), "Высота", "Height should have a Russian property description.");
        RequireContains(PropertyDescriptionProvider.GetDescription("Background"), "Цвет фона", "Background should have a Russian property description.");
        RequireContains(PropertyDescriptionProvider.GetDescription("CanUserSortColumns"), "сортировку", "DataGrid sorting property should be explained.");
        RequireContains(PropertyDescriptionProvider.GetDescription("CanUserResizeColumns"), "ширину колонок", "DataGrid resize property should be explained.");
        RequireContains(PropertyDescriptionProvider.GetDescription("TextWrapping"), "Переносить", "DataGrid wrapping property should be explained.");
        RequireContains(PropertyDescriptionProvider.GetDescription("TextTrimming"), "Обрезать", "DataGrid trimming property should be explained.");
    }

    private static void ConfigurePropertyInspectorUsesRussianPropertyDescriptionTooltips(MainWindowViewModel vm)
    {
        ConfigureSimpleFormExport(vm);
        var button = vm.Controls.Single(control => string.Equals(control.Name, "SaveButton", StringComparison.Ordinal));
        vm.SelectSingleControl(button);
    }

    private static void AssertPropertyInspectorUsesRussianPropertyDescriptionTooltips(SmokeContext context)
    {
        var rows = context.ViewModel.PropertyGridCategories.SelectMany(category => category.Rows).ToList();
        var width = rows.SingleOrDefault(row => string.Equals(row.Label, "Width", StringComparison.Ordinal));
        if (width is null)
            throw new InvalidOperationException("Property Inspector should contain Width row.");
        RequireContains(width.Description, "Ширина элемента", "Width row should display the centralized Russian description.");
        RequireNotContains(width.Description, "Element width", "Width row should not keep the old English description.");

        var text = rows.SingleOrDefault(row => string.Equals(row.Label, "Text / Content", StringComparison.Ordinal));
        if (text is null)
            throw new InvalidOperationException("Property Inspector should contain Text / Content row.");
        RequireContains(text.Description, "Текст", "Text / Content row should display the centralized Russian description.");

        if (!context.ViewModel.PropertyGridCategories.Any(category => string.Equals(category.Title, "Основные", StringComparison.Ordinal)))
            throw new InvalidOperationException("PropertyGrid Common category should be localized.");
        if (!context.ViewModel.PropertyGridCategories.Any(category => string.Equals(category.Title, "Внешний вид", StringComparison.Ordinal)))
            throw new InvalidOperationException("PropertyGrid Appearance category should be localized.");
    }

    private static void ConfigurePropertyInspectorEnumEditorsUseComboBox(MainWindowViewModel vm)
    {
        ConfigureSimpleFormExport(vm);

        vm.ClearSelection();
        var windowStateRow = GetPropertyGridRow(vm, nameof(MainWindowViewModel.FormWindowState));
        var maximizedOption = windowStateRow.Options.FirstOrDefault(option => string.Equals(option.Value, MainWindowViewModel.WindowStateMaximized, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("WindowState enum row should expose Maximized/working-area option.");
        windowStateRow.SelectedOption = maximizedOption;
        RequireEqual(MainWindowViewModel.WindowStateMaximized, vm.FormWindowState, "WindowState ComboBox selection should apply through PropertyGrid row.");

        var button = vm.Controls.Single(control => string.Equals(control.Name, "SaveButton", StringComparison.Ordinal));
        vm.SelectSingleControl(button);
        var horizontalAlignmentRow = GetPropertyGridRow(vm, nameof(DesignControlModel.HorizontalAlignment));
        var centerOption = horizontalAlignmentRow.Options.FirstOrDefault(option => string.Equals(option.Value, "Center", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("HorizontalAlignment enum row should expose Center option.");
        horizontalAlignmentRow.SelectedOption = centerOption;
        RequireEqual("Center", button.HorizontalAlignment, "HorizontalAlignment ComboBox selection should apply to the selected control.");
    }

    private static void AssertPropertyInspectorEnumEditorsUseComboBox(SmokeContext context)
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "MainWindow.axaml"), Encoding.UTF8);
        var code = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "MainWindow.axaml.cs"), Encoding.UTF8);

        RequireContains(xaml, "IsVisible=\"{Binding IsEnumEditor}\"", "Property Inspector should still render enum editor blocks.");
        RequireContains(xaml, "SelectedItem=\"{Binding SelectedOption, Mode=TwoWay}\"", "Enum editors should bind ComboBox selection directly to SelectedOption.");
        RequireContains(xaml, "ItemsSource=\"{Binding Options}\"", "Enum editors should display the row Options collection inline.");
        RequireContains(xaml, "x:DataType=\"vm:PropertyGridOptionViewModel\"", "PropertyGrid enum ComboBox should display option titles.");
        RequireContains(xaml, "x:DataType=\"contracts:PropertyOption\"", "Descriptor enum ComboBox should display descriptor option titles.");
        RequireNotContains(xaml, "Click=\"PropertyGridOptionButton_Click\"", "PropertyGrid enum editor must not open a modal option dialog.");
        RequireNotContains(xaml, "Click=\"OpenDescriptorEnumButton_Click\"", "Descriptor enum editor must not open a modal option dialog.");
        RequireNotContains(code, "ShowPropertyGridOptionDialogAsync", "PropertyGrid modal option dialog code should be removed.");
        RequireNotContains(code, "ShowDescriptorOptionDialogAsync", "Descriptor modal option dialog code should be removed.");
        RequireNotContains(code, "OPTION_DIALOG_OPENED", "PropertyGrid option dialog diagnostics should not remain.");
    }

    private static void RequireTechnicalName(IReadOnlySet<string> names, string expected)
    {
        if (!names.Contains(expected))
            throw new InvalidOperationException($"Technical name '{expected}' is missing.");
    }

    private static void RequireEqual(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException($"{message} Expected '{expected}', got '{actual}'.");
    }

    private static void AssertSimpleFormExport(SmokeContext context)
    {
        RequireContains(context.Xaml, "<Button", "XAML should contain Button.");
        RequireContains(context.Xaml, "<TextBox", "XAML should contain TextBox.");
        RequireContains(context.Xaml, "<CheckBox", "XAML should contain CheckBox.");
        RequireContains(context.Xaml, "<TextBlock", "XAML should contain TextBlock.");
        RequireContains(context.Xaml, "<Border", "XAML should contain Border.");
        RequireNotContains(context.Xaml, "Avalonia.Controls.DataGrid", "Simple export must not require DataGrid package.");
        RequireContains(context.ChecklistText, "Plugins: none", "Simple export checklist should not require plugins.");
    }

    private static void ConfigureLayoutPropertiesAvailableInPropertyInspector(MainWindowViewModel vm)
    {
        ConfigureSimpleFormExport(vm);
        var button = vm.Controls.Single(control => string.Equals(control.Name, "SaveButton", StringComparison.Ordinal));
        vm.SelectSingleControl(button);
    }

    private static void AssertLayoutPropertiesAvailableInPropertyInspector(SmokeContext context)
    {
        var rows = context.ViewModel.PropertyGridCategories.SelectMany(category => category.Rows).ToList();
        var keys = rows.Select(row => row.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var expected in new[]
        {
            nameof(DesignControlModel.Width),
            nameof(DesignControlModel.Height),
            nameof(DesignControlModel.X),
            nameof(DesignControlModel.Y),
            nameof(DesignControlModel.Margin),
            nameof(DesignControlModel.HorizontalAlignment),
            nameof(DesignControlModel.VerticalAlignment)
        })
        {
            if (!keys.Contains(expected))
                throw new InvalidOperationException($"Property Inspector should expose layout property '{expected}' in Properties.");
        }

        if (!rows.Any(row => string.Equals(row.Category, "Layout", StringComparison.Ordinal)
            && string.Equals(row.Key, nameof(DesignControlModel.X), StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Canvas position properties should be grouped under Layout inside Properties.");
        }
    }

    private static void AssertLayoutTabCanBeHiddenWithoutBreakingProperties(SmokeContext context)
    {
        var vm = context.ViewModel;
        if (vm.EnableExperimentalLayoutTab)
            throw new InvalidOperationException("Experimental Layout tab should be disabled by default.");
        if (vm.IsLayoutTabVisible)
            throw new InvalidOperationException("Layout tab should be hidden when the feature flag is disabled.");
        if (vm.AvailableWorkspaceModes.Contains(MainWindowViewModel.WorkspaceModeLayout))
            throw new InvalidOperationException("Layout workspace mode should be absent when the feature flag is disabled.");

        AssertLayoutPropertiesAvailableInPropertyInspector(context);

        vm.EnableExperimentalLayoutTab = true;
        if (!vm.IsLayoutTabVisible || !vm.AvailableWorkspaceModes.Contains(MainWindowViewModel.WorkspaceModeLayout))
            throw new InvalidOperationException("Layout tab should be restorable through the experimental feature flag.");
        vm.EnableExperimentalLayoutTab = false;
        if (vm.WorkspaceMode == MainWindowViewModel.WorkspaceModeLayout)
            throw new InvalidOperationException("Disabling the Layout tab should leave the workspace in a non-Layout mode.");
    }

    private static void ConfigureRuntimeBadgeDoesNotAffectPreviewBounds(MainWindowViewModel vm)
    {
        ConfigureResizePreviewWindowDoesNotChangeControlPositions(vm);
    }

    private static void AssertRuntimeBadgeDoesNotAffectPreviewBounds(SmokeContext context)
    {
        var vm = context.ViewModel;
        vm.ShowPreviewRuntimeBadge = false;
        var withoutBadge = vm.BuildPreviewControlSnapshotsForActiveDocument("RuntimeBadgeOff", 900, 600).ToList();
        vm.ShowPreviewRuntimeBadge = true;
        var withBadge = vm.BuildPreviewControlSnapshotsForActiveDocument("RuntimeBadgeOn", 900, 600).ToList();

        foreach (var off in withoutBadge)
        {
            var on = withBadge.Single(control => control.ControlId == off.ControlId);
            RequireSameBounds(off, on);
        }
    }

    private static void AssertHelpWindowOpensMaximized(SmokeContext context)
    {
        RequireEqual(WindowState.Maximized.ToString(), HelpWindow.PreferredWindowState.ToString(), "Help Window should prefer maximized startup.");
        RequireContains(
            File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "HelpWindow.axaml"), Encoding.UTF8),
            "WindowState=\"Maximized\"",
            "HelpWindow.axaml should open maximized.");
    }

    private static void AssertHelpWindowOpensLargeAdaptive(SmokeContext context)
    {
        var fullHd = HelpWindow.CalculateAdaptiveSize(1920, 1080);
        if (fullHd.Width < 1840 || fullHd.Width > 1920)
            throw new InvalidOperationException($"Help window width should use most of Full HD working area, got {fullHd.Width}.");
        if (fullHd.Height < 1000 || fullHd.Height > 1080)
            throw new InvalidOperationException($"Help window height should use most of Full HD working area, got {fullHd.Height}.");

        var small = HelpWindow.CalculateAdaptiveSize(1000, 680);
        if (small.Width > 1000 || small.Height > 680)
            throw new InvalidOperationException($"Help window must not exceed small working area, got {small.Width}x{small.Height}.");
    }

    private static void AssertHelpWindowReusesExistingInstance(SmokeContext context)
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "MainWindow.axaml.cs"), Encoding.UTF8);
        RequireContains(source, "_helpWindow is { IsVisible: true }", "OpenHelpWindow should check an existing visible HelpWindow.");
        RequireContains(source, "_helpWindow.Activate();", "OpenHelpWindow should activate the existing HelpWindow.");
        RequireContains(source, "action=activate-existing", "OpenHelpWindow should emit reuse diagnostics.");
    }

    private static void AssertHelpSectionsExist(SmokeContext context)
    {
        var sections = new HelpContentService().CreateSections();
        var required = new[]
        {
            "home",
            "quick-start",
            "interface",
            "forms",
            "properties",
            "datagrid",
            "dll-sql",
            "preview-export",
            "diagnostics",
            "developer",
            "tips",
            "about"
        };

        foreach (var id in required)
        {
            if (!sections.Any(section => string.Equals(section.Id, id, StringComparison.Ordinal)))
                throw new InvalidOperationException($"Help Center should contain section '{id}'.");
        }
    }

    private static void AssertHelpCarouselHasSlides(SmokeContext context)
    {
        var slides = new HelpContentService().CreateCarouselSlides();
        if (slides.Count < 7)
            throw new InvalidOperationException($"Help carousel should contain multiple useful slides, got {slides.Count}.");

        RequireContains(string.Join(Environment.NewLine, slides.Select(slide => slide.Title)), "DataGrid", "Help carousel should include DataGrid slide.");
        RequireContains(string.Join(Environment.NewLine, slides.Select(slide => slide.Title)), "Export", "Help carousel should include Export slide.");
    }

    private static void AssertHelpContentMentionsAlpha30(SmokeContext context)
    {
        var service = new HelpContentService();
        var text = string.Join(Environment.NewLine, service.CreateSections().SelectMany(section =>
            new[]
            {
                section.Title,
                section.Subtitle
            }
            .Concat(section.FeatureCards.Select(card => $"{card.Title} {card.Description}"))
            .Concat(section.Articles.Select(article => $"{article.Title} {article.Body} {string.Join(" ", article.Bullets)}"))));

        RequireContains(text, "Alpha 3.0", "Help Center content should be updated for Alpha 3.0.");
        RequireContains(text, "DataGrid", "Help Center content should describe DataGrid.");
        RequireContains(text, "DLL", "Help Center content should describe DLL import/data sources.");
    }

    private static void AssertHelpAboutProjectDoesNotMentionUniversityPresentation(SmokeContext context)
    {
        var text = BuildHelpContentText().ToLowerInvariant();
        foreach (var forbidden in new[] { "вуз", "презентац", "диплом" })
        {
            if (text.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"User-facing Help Center content should not mention '{forbidden}'.");
        }
    }

    private static void AssertHelpDeveloperSectionExists(SmokeContext context)
    {
        var developer = new HelpContentService().CreateSections().SingleOrDefault(section => string.Equals(section.Id, "developer", StringComparison.Ordinal));
        if (developer is null)
            throw new InvalidOperationException("Help Center should contain developer concepts section.");
        RequireEqual("Для разработчиков", developer.Title, "Developer help section should have Russian title.");
    }

    private static void AssertHelpDeveloperSectionContainsCoreTerms(SmokeContext context)
    {
        var developer = new HelpContentService().CreateSections().Single(section => string.Equals(section.Id, "developer", StringComparison.Ordinal));
        var text = BuildHelpSectionText(developer);
        foreach (var term in new[] { "Canvas", "MVVM", "AXAML", "Binding", "DataContext", "ViewModel", "Export Pipeline", "ItemsSource", "NuGet" })
            RequireContains(text, term, $"Developer section should explain {term}.");
    }

    private static void AssertHelpSearchFindsDeveloperTerms(SmokeContext context)
    {
        foreach (var query in new[] { "MVVM", "Canvas", "Binding", "DataContext", "Export Pipeline", "ItemsSource", "NuGet" })
        {
            var viewModel = new HelpWindowViewModel(new HelpContentService())
            {
                SearchQuery = query
            };

            if (!viewModel.Sections.Any(section => string.Equals(section.Id, "developer", StringComparison.Ordinal)))
                throw new InvalidOperationException($"Help search should find developer section by '{query}'.");
        }
    }

    private static void AssertHelpNavigationChangesSelectedSection(SmokeContext context)
    {
        var viewModel = new HelpWindowViewModel(new HelpContentService());
        var initial = viewModel.SelectedSection?.Id ?? "";
        var dataGrid = viewModel.Sections.Single(section => string.Equals(section.Id, "datagrid", StringComparison.Ordinal));

        viewModel.SelectSectionCommand.Execute(dataGrid);
        RequireEqual("datagrid", viewModel.SelectedSection?.Id ?? "", "Help navigation should select the requested section.");

        viewModel.GoToSectionCommand.Execute("preview-export");
        RequireEqual("preview-export", viewModel.SelectedSection?.Id ?? "", "Help quick actions should navigate by section id.");

        viewModel.SearchQuery = "NuGet";
        if (!viewModel.Sections.Any(section => string.Equals(section.Id, "preview-export", StringComparison.Ordinal)))
            throw new InvalidOperationException("Help search should find Preview / Export by NuGet content.");

        viewModel.ClearSearchCommand.Execute(null);
        if (!viewModel.Sections.Any(section => string.Equals(section.Id, initial, StringComparison.Ordinal)))
            throw new InvalidOperationException("Help clear search should restore all sections.");
    }

    private static void AssertHelpLayoutWorksInMaximizedMode(SmokeContext context)
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "HelpWindow.axaml"), Encoding.UTF8);
        RequireContains(xaml, "ColumnDefinitions=\"260,*\"", "Help layout should keep a fixed sidebar and flexible content area.");
        RequireContains(xaml, "ScrollViewer", "Help layout should keep content scrollable in maximized mode.");
        RequireContains(xaml, "VerticalScrollBarVisibility=\"Auto\"", "Help content should have vertical scrolling available.");
        RequireContains(xaml, "WindowState=\"Maximized\"", "Help layout smoke should verify maximized mode.");
    }

    private static void AssertHelpWindowDoesNotBlockMainDesigner(SmokeContext context)
    {
        var controlsCount = context.ViewModel.Controls.Count;
        var activeFormId = context.ViewModel.ActiveFormDocument?.Id ?? "";
        var generatedXaml = context.ViewModel.GeneratedXaml;

        var help = new HelpWindowViewModel(new HelpContentService());
        help.GoToSectionCommand.Execute("datagrid");
        help.NextSlideCommand.Execute(null);
        help.SearchQuery = "Export";
        help.ClearSearchCommand.Execute(null);

        RequireEqual(controlsCount, context.ViewModel.Controls.Count, "Help Center navigation should not mutate designer controls.");
        RequireEqual(activeFormId, context.ViewModel.ActiveFormDocument?.Id ?? "", "Help Center navigation should not change active form.");
        RequireEqual(generatedXaml, context.ViewModel.GeneratedXaml, "Help Center navigation should not regenerate or mutate export output.");
    }

    private static void AssertSettingsButtonOpensSettingsWindow(SmokeContext context)
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "MainWindow.axaml"), Encoding.UTF8);
        var code = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "MainWindow.axaml.cs"), Encoding.UTF8);

        RequireContains(xaml, "Click=\"OpenSettingsButton_Click\"", "Toolbar should expose a gear/settings button.");
        RequireContains(xaml, "ToolTip.Tip=\"{Binding Texts.Settings}\"", "Settings button tooltip should follow the active UI language.");
        RequireContains(code, "OpenSettingsWindow(", "Settings button should open the Settings window.");
        RequireContains(code, "new SettingsWindow(VM", "MainWindow should create SettingsWindow with the editor ViewModel.");
    }

    private static void AssertSettingsWindowReusesExistingInstance(SmokeContext context)
    {
        var code = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "MainWindow.axaml.cs"), Encoding.UTF8);
        RequireContains(code, "_settingsWindow is { IsVisible: true }", "OpenSettingsWindow should check existing visible SettingsWindow.");
        RequireContains(code, "_settingsWindow.Activate();", "OpenSettingsWindow should activate existing SettingsWindow.");
        RequireContains(code, "action=activate-existing", "Settings reuse should emit diagnostics.");
    }

    private static void AssertExportSettingsMovedOutOfExportPanel(SmokeContext context)
    {
        var mainWindowXaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "MainWindow.axaml"), Encoding.UTF8);
        var settingsXaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "SettingsWindow.axaml"), Encoding.UTF8);

        if (mainWindowXaml.Contains("Показывать runtime-плашку в Preview", StringComparison.Ordinal)
            || mainWindowXaml.Contains("Использовать пользовательский NuGet source", StringComparison.Ordinal)
            || mainWindowXaml.Contains("Export SQL connection string в generated code", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Export panel should not render detailed settings checkboxes directly.");
        }

        RequireContains(mainWindowXaml, "Настройки Export / Build", "Export panel should keep a compact settings summary.");
        RequireContains(mainWindowXaml, "Открыть настройки", "Export panel should provide a Settings shortcut.");
        RequireContains(settingsXaml, "Texts.RuntimeBadge", "Runtime badge setting should live in SettingsWindow.");
        RequireContains(settingsXaml, "Texts.UseCustomNuGetSource", "NuGet setting should live in SettingsWindow.");
        RequireContains(settingsXaml, "Texts.ExportSqlConnectionString", "SQL export security setting should live in SettingsWindow.");
    }

    private static void AssertSettingsSectionsExist(SmokeContext context)
    {
        var viewModel = new SettingsWindowViewModel(context.ViewModel, Path.Combine(Path.GetTempPath(), "app-settings.json"));
        var sectionTitles = string.Join(Environment.NewLine, viewModel.Sections.Select(section => section.Title));

        foreach (var title in new[]
                 {
                     "Общие",
                     "Интерфейс",
                     "Preview",
                     "Export / Build",
                     "NuGet",
                     "SQL Server",
                     "Logs / Diagnostics",
                     "Advanced"
                 })
        {
            RequireContains(sectionTitles, title, $"Settings should contain section '{title}'.");
        }
    }

    private static void AssertSqlSettingsBuildWindowsAuthConnectionString(SmokeContext context)
    {
        var service = new SqlConnectionStringBuilderService();
        var result = service.Build(new SqlServerSettingsModel
        {
            ServerName = "localhost",
            DatabaseName = "DemoDb",
            AuthenticationMode = SqlServerSettingsModel.AuthWindows,
            TrustServerCertificate = true,
            EncryptConnection = false
        });

        if (!result.Success)
            throw new InvalidOperationException($"Windows auth SQL connection string should build: {result.ErrorMessage}");

        RequireContains(result.ConnectionString, "Data Source=localhost", "Connection string should include Server name.");
        RequireContains(result.ConnectionString, "Initial Catalog=DemoDb", "Connection string should include Database name.");
        RequireContains(result.ConnectionString, "Integrated Security=True", "Windows auth should use Integrated Security.");
        RequireContains(result.ConnectionString, "Trust Server Certificate=True", "Trust Server Certificate should be preserved.");
    }

    private static void AssertSqlSettingsBuildSqlLoginConnectionString(SmokeContext context)
    {
        var service = new SqlConnectionStringBuilderService();
        var result = service.Build(new SqlServerSettingsModel
        {
            ServerName = "sql.local",
            DatabaseName = "DemoDb",
            AuthenticationMode = SqlServerSettingsModel.AuthSqlLogin,
            UserName = "designer",
            Password = "super-secret",
            TrustServerCertificate = true
        });

        if (!result.Success)
            throw new InvalidOperationException($"SQL login connection string should build: {result.ErrorMessage}");

        RequireContains(result.ConnectionString, "User ID=designer", "SQL login should include User Id.");
        RequireContains(result.ConnectionString, "Password=super-secret", "SQL login should include password in effective connection string.");
        if (result.MaskedConnectionString.Contains("super-secret", StringComparison.Ordinal))
            throw new InvalidOperationException("Masked connection string must not expose password.");
        RequireContains(result.MaskedConnectionString, "Password=***", "Masked connection string should show password placeholder.");
    }

    private static void AssertDataSqlPanelUsesGlobalSettings(SmokeContext context)
    {
        var vm = context.ViewModel;
        vm.UseGlobalSqlServerSettings = true;
        vm.SqlServerName = "localhost";
        vm.SqlDatabaseName = "DemoDb";
        vm.SqlAuthenticationMode = SqlServerSettingsModel.AuthWindows;
        vm.SqlTrustServerCertificate = true;

        var source = new BindingSourceModel
        {
            Name = "Orders",
            SourceKind = "SqlServer",
            SourceTableName = "Orders",
            SourceSchemaName = ""
        };

        var resolved = vm.ResolveSqlConnectionStringForSource(source);
        RequireContains(resolved, "Data Source=localhost", "SQL source should resolve Server name from global settings.");
        RequireContains(resolved, "Initial Catalog=DemoDb", "SQL source should resolve Database name from global settings.");

        var changed = vm.TryApplyGlobalSqlSettings(source);
        if (!changed)
            throw new InvalidOperationException("TryApplyGlobalSqlSettings should update an empty SQL source.");
        RequireContains(source.SourceConnectionString, "Data Source=localhost", "Data SQL source should receive resolved connection string.");
        RequireEqual("dbo", source.SourceSchemaName, "Global SQL settings should provide default schema.");
    }

    private static void AssertDataSqlPanelShowsWarningWhenGlobalSqlMissing(SmokeContext context)
    {
        var vm = context.ViewModel;
        vm.SqlServerName = "";
        vm.SqlDatabaseName = "";
        RequireContains(vm.GlobalSqlSettingsStatusText, "SQL Server не настроен", "Missing global SQL config should be visible to the user.");
        RequireContains(vm.DataSqlGlobalSettingsHint, "Настройки", "Data SQL panel hint should direct user to Settings.");
    }

    private static void AssertNuGetSettingsPersist(SmokeContext context)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "FormDesignerSettingsSmoke", Guid.NewGuid().ToString("N"));
        var service = new AppSettingsService(tempDir);
        var settings = new AppSettingsModel
        {
            BuildAndLogs = new BuildAndLogsSettingsModel
            {
                UseCustomNuGetSource = true,
                CustomNuGetSource = "https://nuget.example/v3/index.json",
                AllowInsecureNuGetSource = false,
                IncludeNuGetOrgFallback = true,
                GenerateNuGetConfigInExportedProject = true
            }
        };

        service.SaveAsync(settings).GetAwaiter().GetResult();
        var reloaded = service.Load();

        if (!reloaded.BuildAndLogs.UseCustomNuGetSource
            || !string.Equals(reloaded.BuildAndLogs.CustomNuGetSource, settings.BuildAndLogs.CustomNuGetSource, StringComparison.Ordinal)
            || !reloaded.BuildAndLogs.GenerateNuGetConfigInExportedProject)
        {
            throw new InvalidOperationException("NuGet settings should persist through AppSettingsService.");
        }
    }

    private static void AssertSettingsPersistBetweenRuns(SmokeContext context)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "FormDesignerSettingsSmoke", Guid.NewGuid().ToString("N"));
        var service = new AppSettingsService(tempDir);
        var settings = new AppSettingsModel
        {
            General = new GeneralSettingsModel
            {
                Language = "Русский",
                Theme = "Светлая",
                ConfirmNewProjectWithUnsavedChanges = false,
                EnableRecoveryAutosave = true
            },
            SqlServer = new SqlServerSettingsModel
            {
                ServerName = "localhost",
                DatabaseName = "DemoDb",
                AuthenticationMode = SqlServerSettingsModel.AuthWindows,
                DefaultSchema = "sales",
                DefaultPreviewTopN = 250
            },
            BuildAndLogs = new BuildAndLogsSettingsModel
            {
                VerboseBuildLogs = true,
                LogLevel = "Debug",
                MaxLogFilesCount = 7,
                MaxLogFileSizeMb = 33
            }
        };

        service.SaveAsync(settings).GetAwaiter().GetResult();
        var reloaded = service.Load();

        RequireEqual("localhost", reloaded.SqlServer.ServerName, "SQL Server name should persist between runs.");
        RequireEqual("DemoDb", reloaded.SqlServer.DatabaseName, "SQL Database name should persist between runs.");
        RequireEqual("sales", reloaded.SqlServer.DefaultSchema, "SQL default schema should persist between runs.");
        RequireEqual(250, reloaded.SqlServer.DefaultPreviewTopN, "SQL preview Top N should persist between runs.");
        RequireEqual("Debug", reloaded.BuildAndLogs.LogLevel, "Log level should persist between runs.");
        RequireEqual(7, reloaded.BuildAndLogs.MaxLogFilesCount, "Max log files count should persist between runs.");
        RequireEqual(33, reloaded.BuildAndLogs.MaxLogFileSizeMb, "Max log file size should persist between runs.");
    }

    private static void AssertSettingsLanguageApplyUpdatesSettingsWindow(SmokeContext context)
    {
        var settings = new SettingsWindowViewModel(context.ViewModel, Path.Combine(Path.GetTempPath(), "app-settings.json"));
        settings.SelectedLanguageOption = FindSettingsOption(settings.LanguageOptions, SettingsTextCatalog.LanguageEnglish);

        RequireEqual("Avalonia Designer Settings", settings.Texts.HeaderTitle, "Settings Window should update text immediately when English is selected.");
        RequireEqual("Apply", settings.Texts.Apply, "Apply button text should switch to English.");
        RequireEqual("Unsaved changes", settings.UnsavedStatusText, "Unsaved indicator should be localized after language switch.");

        settings.ApplyCommand.Execute(null);
        RequireEqual(SettingsTextCatalog.LanguageEnglish, context.ViewModel.InterfaceLanguage, "Apply should update main ViewModel language.");
        RequireEqual("Settings", context.ViewModel.Texts.Settings, "Main ViewModel should expose English main UI text after Apply.");
    }

    private static void AssertSettingsLanguagePersistsAfterRestart(SmokeContext context)
    {
        context.ViewModel.InterfaceLanguage = SettingsTextCatalog.LanguageEnglish;
        var captured = context.ViewModel.CaptureGeneralSettings();
        var reloaded = CreateViewModel(context.Scenario.Name + "SettingsLanguageReloaded");
        reloaded.ApplyAppSettings(new AppSettingsModel { General = captured });

        RequireEqual(SettingsTextCatalog.LanguageEnglish, reloaded.InterfaceLanguage, "Selected language should be captured in settings.");
        RequireEqual("Settings", reloaded.Texts.Settings, "Reloaded ViewModel should restore the English UI catalog.");
    }

    private static void AssertSettingsButtonsLocalized(SmokeContext context)
    {
        var settings = new SettingsWindowViewModel(context.ViewModel, Path.Combine(Path.GetTempPath(), "app-settings.json"));
        RequireEqual("Применить", settings.Texts.Apply, "Russian Apply button should be localized.");
        RequireEqual("Сохранить", settings.Texts.Save, "Russian Save button should be localized.");
        RequireEqual("Отмена", settings.Texts.Cancel, "Russian Cancel button should be localized.");
        RequireEqual("Сбросить всё", settings.Texts.ResetAll, "Russian Reset button should be localized.");

        settings.SelectedLanguageOption = FindSettingsOption(settings.LanguageOptions, SettingsTextCatalog.LanguageEnglish);
        RequireEqual("Apply", settings.Texts.Apply, "English Apply button should be localized.");
        RequireEqual("Save", settings.Texts.Save, "English Save button should be localized.");
        RequireEqual("Cancel", settings.Texts.Cancel, "English Cancel button should be localized.");
        RequireEqual("Reset all", settings.Texts.ResetAll, "English Reset button should be localized.");
    }

    private static void AssertSettingsUnsavedIndicatorIsHumanReadable(SmokeContext context)
    {
        var settings = new SettingsWindowViewModel(context.ViewModel, Path.Combine(Path.GetTempPath(), "app-settings.json"));
        RequireNotContains(settings.UnsavedStatusText, "True", "Saved status should not expose a boolean.");
        RequireNotContains(settings.UnsavedStatusText, "False", "Saved status should not expose a boolean.");

        settings.CustomNuGetSource = "https://nuget.example/v3/index.json";
        RequireContains(settings.UnsavedStatusText, "несохран", "Unsaved status should be human-readable in Russian.");
        RequireNotContains(settings.UnsavedStatusText, "True", "Unsaved status should not expose a boolean.");
        RequireNotContains(settings.UnsavedStatusText, "False", "Unsaved status should not expose a boolean.");
    }

    private static void AssertSettingsCancelDiscardsDraftChanges(SmokeContext context)
    {
        context.ViewModel.InterfaceLanguage = SettingsTextCatalog.LanguageRussian;
        var settings = new SettingsWindowViewModel(context.ViewModel, Path.Combine(Path.GetTempPath(), "app-settings.json"));
        var closeRequested = false;
        settings.CloseRequested += (_, _) => closeRequested = true;

        settings.SelectedLanguageOption = FindSettingsOption(settings.LanguageOptions, SettingsTextCatalog.LanguageEnglish);
        settings.CancelCommand.Execute(null);

        if (!closeRequested)
            throw new InvalidOperationException("Cancel should request closing the Settings window.");
        RequireEqual(SettingsTextCatalog.LanguageRussian, context.ViewModel.InterfaceLanguage, "Cancel should discard draft language changes.");
    }

    private static void AssertSettingsApplyKeepsWindowOpenAndAppliesValues(SmokeContext context)
    {
        var settings = new SettingsWindowViewModel(context.ViewModel, Path.Combine(Path.GetTempPath(), "app-settings.json"));
        var closeRequested = false;
        settings.CloseRequested += (_, _) => closeRequested = true;

        settings.SqlServerName = "localhost";
        settings.SqlDatabaseName = "DemoDb";
        settings.ApplyCommand.Execute(null);

        if (closeRequested)
            throw new InvalidOperationException("Apply should not close the Settings window.");
        RequireEqual("localhost", context.ViewModel.SqlServerName, "Apply should copy SQL Server name to the main ViewModel.");
        RequireEqual("DemoDb", context.ViewModel.SqlDatabaseName, "Apply should copy SQL Database name to the main ViewModel.");
        if (settings.IsDirty)
            throw new InvalidOperationException("Apply should clear dirty state after applying settings.");
    }

    private static void AssertSettingsSavePersistsValues(SmokeContext context)
    {
        var settings = new SettingsWindowViewModel(context.ViewModel, Path.Combine(Path.GetTempPath(), "app-settings.json"));
        var saveRequested = false;
        settings.SaveRequested += (_, _) => saveRequested = true;

        settings.UseCustomNuGetSource = true;
        settings.CustomNuGetSource = "https://nuget.example/v3/index.json";
        settings.SaveCommand.Execute(null);

        if (!saveRequested)
            throw new InvalidOperationException("Save should request persistence through the window.");
        if (!context.ViewModel.UseCustomNuGetSource)
            throw new InvalidOperationException("Save should apply NuGet settings before persistence.");
        RequireEqual("https://nuget.example/v3/index.json", context.ViewModel.CustomNuGetSource, "Save should copy custom NuGet source to the main ViewModel.");
    }

    private static void AssertSettingsNoDeadInteractiveOptions(SmokeContext context)
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "SettingsWindow.axaml"), Encoding.UTF8);
        RequireContains(xaml, "Texts.ExperimentalDisabled", "Experimental Advanced settings should be visibly marked.");
        RequireContains(xaml, "IsEnabled=\"False\"", "Experimental Advanced options should be disabled instead of pretending to work.");
        RequireNotContains(xaml, "Изменения: True", "Settings UI should not show raw boolean status.");
        RequireNotContains(xaml, "Reset to defaults", "Settings buttons should be localized through the text catalog.");
    }

    private static void AssertSettingsSqlWindowsAuthHidesCredentials(SmokeContext context)
    {
        var settings = new SettingsWindowViewModel(context.ViewModel, Path.Combine(Path.GetTempPath(), "app-settings.json"));
        settings.SelectedSqlAuthenticationOption = FindSettingsOption(settings.SqlAuthenticationOptions, SqlServerSettingsModel.AuthWindows);
        if (settings.IsSqlLoginSelected)
            throw new InvalidOperationException("Windows Authentication should hide SQL Login credential fields.");

        settings.SelectedSqlAuthenticationOption = FindSettingsOption(settings.SqlAuthenticationOptions, SqlServerSettingsModel.AuthSqlLogin);
        if (!settings.IsSqlLoginSelected)
            throw new InvalidOperationException("SQL Login should show credential fields.");

        var xaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "SettingsWindow.axaml"), Encoding.UTF8);
        RequireContains(xaml, "IsVisible=\"{Binding IsSqlLoginSelected}\"", "SQL credential fields should be hidden for Windows Authentication.");
    }

    private static void AssertSettingsNugetSectionLocalizedAndPersists(SmokeContext context)
    {
        var settings = new SettingsWindowViewModel(context.ViewModel, Path.Combine(Path.GetTempPath(), "app-settings.json"));
        settings.SelectedLanguageOption = FindSettingsOption(settings.LanguageOptions, SettingsTextCatalog.LanguageEnglish);
        settings.SelectSection("nuget");

        RequireEqual("Use custom NuGet source", settings.Texts.UseCustomNuGetSource, "NuGet setting label should be localized in English.");
        RequireEqual("NuGet", settings.SectionTitle, "NuGet section title should stay technical and searchable.");

        settings.UseCustomNuGetSource = true;
        settings.CustomNuGetSource = "https://nuget.example/v3/index.json";
        settings.AllowInsecureNuGetSource = false;
        settings.IncludeNuGetOrgFallback = true;
        settings.GenerateNuGetConfigInExportedProject = true;
        settings.ApplyCommand.Execute(null);

        if (!context.ViewModel.UseCustomNuGetSource)
            throw new InvalidOperationException("NuGet section should apply custom source flag.");
        RequireEqual("https://nuget.example/v3/index.json", context.ViewModel.CustomNuGetSource, "NuGet section should apply custom source value.");
        if (!context.ViewModel.IncludeNuGetOrgFallback || !context.ViewModel.GenerateNuGetConfigInExportedProject)
            throw new InvalidOperationException("NuGet fallback/config settings should apply and persist in the main ViewModel.");
    }

    private static void AssertSettingsContentScrollViewerHasFiniteViewport(SmokeContext context)
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "SettingsWindow.axaml"), Encoding.UTF8);
        RequireContains(xaml, "<Grid RowDefinitions=\"Auto,*,Auto\"", "Settings root should reserve fixed header/footer rows and a finite main row.");
        RequireContains(xaml, "Grid.Row=\"1\"", "Settings should have an explicit main content row.");
        RequireContains(xaml, "ColumnDefinitions=\"270,*\"", "Settings main content should split sidebar and page content.");
        RequireContains(xaml, "x:Name=\"SettingsContentScrollViewer\"", "Selected page content should use a named ScrollViewer.");
        RequireContains(xaml, "HorizontalScrollBarVisibility=\"Disabled\"", "Settings page content should not show horizontal scroll by default.");
        RequireContains(xaml, "VerticalAlignment=\"Stretch\"", "Settings page ScrollViewer should stretch to the finite viewport.");
        RequireContains(xaml, "ClipToBounds=\"True\"", "Settings layout should clip content to the viewport instead of drawing under the footer.");
        RequireEqual(1, CountOccurrences(xaml, "<ScrollViewer"), "Only selected page content should be an explicit ScrollViewer in SettingsWindow XAML.");
    }

    private static void AssertSettingsFooterOutsideScrollableContent(SmokeContext context)
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "SettingsWindow.axaml"), Encoding.UTF8);
        var scrollStart = xaml.IndexOf("x:Name=\"SettingsContentScrollViewer\"", StringComparison.Ordinal);
        var scrollEnd = xaml.IndexOf("</ScrollViewer>", scrollStart, StringComparison.Ordinal);
        var footerStart = xaml.IndexOf("<Border Grid.Row=\"2\"", StringComparison.Ordinal);

        if (scrollStart < 0 || scrollEnd < 0 || footerStart < 0)
            throw new InvalidOperationException("Settings XAML should contain named content ScrollViewer and footer row.");
        if (footerStart < scrollEnd)
            throw new InvalidOperationException("Settings footer must stay outside selected page ScrollViewer.");
    }

    private static void AssertSettingsSectionChangeResetsContentScroll(SmokeContext context)
    {
        var code = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "SettingsWindow.axaml.cs"), Encoding.UTF8);
        RequireContains(code, "SettingsViewModel_PropertyChanged", "Settings window should listen for selected section changes.");
        RequireContains(code, "SettingsContentScrollViewer.Offset = new Vector(0, 0)", "Changing Settings section should reset page scroll to the top.");
        RequireContains(code, "SettingsContentScrollViewer.InvalidateMeasure()", "Changing Settings section should invalidate content measure.");
        RequireContains(code, "SETTINGS_CONTENT_SCROLL_RESET", "Settings section scroll reset should be diagnostic-friendly.");
    }

    private static SettingsOptionModel FindSettingsOption(IEnumerable<SettingsOptionModel> options, string value)
    {
        return options.Single(option => string.Equals(option.Value, value, StringComparison.Ordinal));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string BuildHelpContentText()
    {
        return string.Join(Environment.NewLine, new HelpContentService().CreateSections().Select(BuildHelpSectionText));
    }

    private static string BuildHelpSectionText(HelpSection section)
    {
        return string.Join(Environment.NewLine,
            new[]
            {
                section.Id,
                section.Title,
                section.Subtitle
            }
            .Concat(section.FeatureCards.Select(card => $"{card.Title} {card.Description} {card.Icon}"))
            .Concat(section.QuickActions.Select(action => $"{action.Title} {action.Description} {action.TargetSectionId}"))
            .Concat(section.Articles.Select(article => $"{article.Title} {article.Body} {string.Join(" ", article.Bullets)}")));
    }

    private static void AssertValidateBuildShowsRestoreAndBuildSteps(SmokeContext context)
    {
        var (result, messages) = RunValidateBuildForSmoke(context.ViewModel, context.Scenario.Name);
        RequireContains(result.StepSummary, "Preparing export workspace", "Validate Build should report workspace preparation.");
        RequireContains(result.StepSummary, "Generating project files", "Validate Build should report file generation.");
        RequireContains(result.StepSummary, "Restoring NuGet packages", "Validate Build should report restore step.");
        RequireContains(result.StepSummary, "Building project", "Validate Build should report build step.");
        RequireContains(result.StepSummary, "Collecting warnings/errors", "Validate Build should report diagnostics collection.");
        if (!messages.Any(message => message.Contains("VALIDATE_BUILD_STEP_START", StringComparison.Ordinal)))
            throw new InvalidOperationException("Validate Build should emit step start logs.");
    }

    private static void AssertValidateBuildStoresDetailedLogs(SmokeContext context)
    {
        var (result, _) = RunValidateBuildForSmoke(context.ViewModel, context.Scenario.Name);
        if (string.IsNullOrWhiteSpace(result.DetailedLogPath) || !File.Exists(result.DetailedLogPath))
            throw new InvalidOperationException("Validate Build should write a detailed log file.");

        var log = File.ReadAllText(result.DetailedLogPath);
        RequireContains(log, "VALIDATE_BUILD_START", "Detailed build log should include start event.");
        RequireContains(log, "VALIDATE_BUILD_STEP_START", "Detailed build log should include step events.");
        RequireContains(log, "VALIDATE_BUILD_COMMAND", "Detailed build log should include dotnet commands.");
        RequireContains(log, "VALIDATE_BUILD_END", "Detailed build log should include end event.");
    }

    private static void AssertValidateBuildSettingsCanDisableAutoBuild(SmokeContext context)
    {
        var vm = context.ViewModel;
        vm.ValidateBuildAfterExport = false;
        vm.GenerateXaml();
        if (vm.CurrentExportBuildValidation.Status == ExportBuildValidationStatus.Building)
            throw new InvalidOperationException("Export generation should not start Validate Build when auto-build is disabled.");

        var settings = vm.CaptureBuildAndLogsSettings();
        if (settings.ValidateBuildAfterExport)
            throw new InvalidOperationException("ValidateBuildAfterExport setting should persist disabled state.");
    }

    private static void AssertValidateBuildUsesDefaultNugetSourceWhenCustomEmpty(SmokeContext context)
    {
        var vm = context.ViewModel;
        vm.UseCustomNuGetSource = false;
        vm.CustomNuGetSource = "";

        var (result, messages) = RunValidateBuildForSmoke(vm, context.Scenario.Name);
        var nugetConfig = File.ReadAllText(Path.Combine(result.ProjectPath, "NuGet.config"), Encoding.UTF8);
        RequireContains(nugetConfig, "https://api.nuget.org/v3/index.json", "Default Validate Build should use NuGet.org.");
        RequireContains(result.StepSummary, "custom=False", "Validate Build summary should mark default NuGet source as not custom.");
        if (!messages.Any(message => message.Contains("VALIDATE_BUILD_NUGET_SOURCE_SELECTED", StringComparison.Ordinal)
                                     && message.Contains("custom=False", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Validate Build should log default NuGet source selection.");
        }
    }

    private static void AssertValidateBuildUsesCustomHttpsNugetSource(SmokeContext context)
    {
        var vm = context.ViewModel;
        vm.UseCustomNuGetSource = true;
        vm.CustomNuGetSource = "https://api.nuget.org/v3/index.json";
        vm.AllowInsecureNuGetSource = false;
        vm.IncludeNuGetOrgFallback = false;

        var (result, messages) = RunValidateBuildForSmoke(vm, context.Scenario.Name);
        var nugetConfig = File.ReadAllText(Path.Combine(result.ProjectPath, "NuGet.config"), Encoding.UTF8);
        RequireContains(nugetConfig, "key=\"CustomNuGetSource\"", "Custom HTTPS source should be named in generated NuGet.config.");
        RequireContains(nugetConfig, "https://api.nuget.org/v3/index.json", "Custom HTTPS source should be written to generated NuGet.config.");
        RequireContains(result.StepSummary, "custom=True", "Validate Build summary should mark custom NuGet source.");
        if (!messages.Any(message => message.Contains("VALIDATE_BUILD_COMMAND", StringComparison.Ordinal)
                                     && message.Contains("--configfile", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Validate Build restore should explicitly use generated NuGet.config.");
        }
    }

    private static void AssertValidateBuildUsesCustomHttpNugetSourceWithAllowInsecure(SmokeContext context)
    {
        var vm = context.ViewModel;
        vm.UseCustomNuGetSource = true;
        vm.CustomNuGetSource = "http://127.0.0.1:9/v3/index.json";
        vm.AllowInsecureNuGetSource = true;

        var (result, messages) = RunValidateBuildForSmoke(vm, context.Scenario.Name, requirePassed: false);
        if (string.IsNullOrWhiteSpace(result.ProjectPath) || !Directory.Exists(result.ProjectPath))
            throw new InvalidOperationException("Validate Build should still create a validation project for allowed HTTP source.");

        var nugetConfig = File.ReadAllText(Path.Combine(result.ProjectPath, "NuGet.config"), Encoding.UTF8);
        RequireContains(nugetConfig, "value=\"http://127.0.0.1:9/v3/index.json\"", "Custom HTTP source should be written to generated NuGet.config.");
        RequireContains(nugetConfig, "allowInsecureConnections=\"true\"", "Allowed HTTP source should generate allowInsecureConnections.");
        RequireContains(nugetConfig, "https://api.nuget.org/v3/index.json", "Custom source validation config should include nuget.org fallback by default.");
        if (!messages.Any(message => message.Contains("NUGET_CONFIG_GENERATED_FOR_VALIDATE_BUILD", StringComparison.Ordinal)
                                     && message.Contains("allowInsecure=", StringComparison.Ordinal)
                                     && message.Contains("True", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Validate Build should log generated NuGet.config for allowed HTTP source.");
        }
    }

    private static void AssertValidateBuildBlocksHttpSourceWithoutAllowInsecure(SmokeContext context)
    {
        var vm = context.ViewModel;
        vm.UseCustomNuGetSource = true;
        vm.CustomNuGetSource = "http://127.0.0.1:9/v3/index.json";
        vm.AllowInsecureNuGetSource = false;

        var (result, messages) = RunValidateBuildForSmoke(vm, context.Scenario.Name, requirePassed: false);
        if (result.Status != ExportBuildValidationStatus.Failed || result.ExitCode != -1)
            throw new InvalidOperationException("HTTP custom source without allowInsecureConnections should be blocked before restore.");
        RequireContains(result.Output, "allowInsecureConnections", "Blocked HTTP source should explain allowInsecureConnections.");
        if (!messages.Any(message => message.Contains("NUGET_HTTP_SOURCE_REQUIRES_ALLOW_INSECURE", StringComparison.Ordinal)))
            throw new InvalidOperationException("Blocked HTTP source should emit NUGET_HTTP_SOURCE_REQUIRES_ALLOW_INSECURE.");
        if (messages.Any(message => message.Contains("VALIDATE_BUILD_COMMAND", StringComparison.Ordinal)))
            throw new InvalidOperationException("Blocked HTTP source should not run dotnet restore silently.");
    }

    private static void AssertNugetSourceSettingsPersist(SmokeContext context)
    {
        var vm = context.ViewModel;
        vm.UseCustomNuGetSource = true;
        vm.CustomNuGetSource = @"C:\local-nuget-feed";
        vm.AllowInsecureNuGetSource = true;
        vm.IncludeNuGetOrgFallback = false;
        vm.GenerateNuGetConfigInExportedProject = true;

        var settings = new AppSettingsModel
        {
            BuildAndLogs = vm.CaptureBuildAndLogsSettings()
        };
        var reloaded = CreateViewModel(context.Scenario.Name + "Reloaded");
        reloaded.ApplyAppSettings(settings);

        if (!reloaded.UseCustomNuGetSource
            || !string.Equals(reloaded.CustomNuGetSource, vm.CustomNuGetSource, StringComparison.Ordinal)
            || !reloaded.AllowInsecureNuGetSource
            || reloaded.IncludeNuGetOrgFallback
            || !reloaded.GenerateNuGetConfigInExportedProject)
        {
            throw new InvalidOperationException("Custom NuGet source settings should persist through AppSettings.");
        }
    }

    private static void AssertValidateBuildOutputShowsNugetSource(SmokeContext context)
    {
        var vm = context.ViewModel;
        vm.UseCustomNuGetSource = true;
        vm.CustomNuGetSource = "https://api.nuget.org/v3/index.json";

        var (result, messages) = RunValidateBuildForSmoke(vm, context.Scenario.Name);
        RequireContains(result.StepSummary, "NuGet source:", "Validate Build summary should show selected NuGet source.");
        RequireContains(result.StepSummary, "NuGet.config:", "Validate Build summary should show generated NuGet.config path.");
        if (!messages.Any(message => message.Contains("VALIDATE_BUILD_NUGET_SOURCE_SELECTED", StringComparison.Ordinal)))
            throw new InvalidOperationException("Validate Build output should include selected NuGet source diagnostic.");
    }

    private static void AssertLogsPanelShowsExportBuildDllErrors(SmokeContext context)
    {
        var vm = context.ViewModel;
        vm.LogWorkspace(WorkspaceLogLevel.Error, MainWindowViewModel.OutputCategoryExport, "Export failed smoke.", "export details");
        vm.LogWorkspace(WorkspaceLogLevel.Warning, MainWindowViewModel.OutputCategoryPlugins, "DLL import warning smoke.", "dll details");

        if (!vm.OutputEntries.Any(entry => entry.Category == MainWindowViewModel.OutputCategoryExport && entry.Level == WorkspaceLogLevel.Error))
            throw new InvalidOperationException("Logs panel should include Export errors.");
        if (!vm.OutputEntries.Any(entry => entry.Category == MainWindowViewModel.OutputCategoryPlugins && entry.Level == WorkspaceLogLevel.Warning))
            throw new InvalidOperationException("Logs panel should include DLL/plugin warnings.");
    }

    private static (ExportBuildValidationResult Result, List<string> Messages) RunValidateBuildForSmoke(MainWindowViewModel vm, string scenarioName, bool requirePassed = true)
    {
        var validationRoot = Path.Combine(Path.GetTempPath(), "FormDesignerSmokeValidation", scenarioName, Guid.NewGuid().ToString("N"));
        var messages = new List<string>();
        var result = vm.ValidateCurrentExportBuildAsync(validationRoot, message =>
        {
            messages.Add(message);
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();

        if (requirePassed && result.Status != ExportBuildValidationStatus.Passed)
            throw new InvalidOperationException($"Validation build did not pass: {result.Status} {result.Output}");

        return (result, messages);
    }

    private static void ConfigureCanvasBorderBackgroundZOrderExport(MainWindowViewModel vm)
    {
        vm.SurfaceLayoutMode = DesignerLayoutModes.Absolute;
        vm.Controls.Add(Control(DesignerControlTypes.TextBlock, "CardTitle", 54, 46, 220, 30, text: "Customer"));
        vm.Controls.Add(Control(DesignerControlTypes.TextBox, "CustomerNameTextBox", 54, 92, 260, 38, placeholder: "Name"));
        vm.Controls.Add(Control(DesignerControlTypes.Border, "BackgroundPanel", 24, 28, 520, 180, background: "#EFF6FF", border: "#93C5FD", radius: 16));
    }

    private static void AssertCanvasBorderBackgroundZOrderExport(SmokeContext context)
    {
        var borderIndex = context.Xaml.IndexOf("x:Name=\"BackgroundPanel\"", StringComparison.Ordinal);
        var textBlockIndex = context.Xaml.IndexOf("x:Name=\"CardTitle\"", StringComparison.Ordinal);
        var textBoxIndex = context.Xaml.IndexOf("x:Name=\"CustomerNameTextBox\"", StringComparison.Ordinal);
        if (borderIndex < 0 || textBlockIndex < 0 || textBoxIndex < 0)
            throw new InvalidOperationException("Z-order smoke XAML should contain Border, TextBlock and TextBox.");

        if (borderIndex > textBlockIndex || borderIndex > textBoxIndex)
            throw new InvalidOperationException("Background Border should be exported before foreground text controls.");

        RequireContains(context.Xaml, "x:Name=\"BackgroundPanel\" Width=\"520\" Height=\"180\" Canvas.Left=\"24\" Canvas.Top=\"28\" ZIndex=\"0\"", "Background Border should export ZIndex=0.");
        RequireContains(context.Xaml, "x:Name=\"CardTitle\" Text=\"Customer\" Width=\"220\" Height=\"30\" Canvas.Left=\"54\" Canvas.Top=\"46\" ZIndex=\"1\"", "TextBlock should export above background Border.");
        RequireContains(context.Xaml, "x:Name=\"CustomerNameTextBox\"", "TextBox should be exported.");
        if (context.Xaml.Contains("<Canvas>\r\n      </Canvas>", StringComparison.Ordinal)
            || context.Xaml.Contains("<Canvas>\n      </Canvas>", StringComparison.Ordinal)
            || context.Xaml.Contains("<Canvas>\r\n        </Canvas>", StringComparison.Ordinal)
            || context.Xaml.Contains("<Canvas>\n        </Canvas>", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Export should not generate an empty inner Canvas for a background Border.");
        }
    }

    private static void ConfigurePreviewExportConsistencyForm(MainWindowViewModel vm)
    {
        vm.SurfaceLayoutMode = DesignerLayoutModes.Absolute;
        vm.Controls.Add(Control(DesignerControlTypes.TextBlock, "CardTitle", 54, 46, 220, 30, text: "Customer", foreground: "#111827"));
        vm.Controls.Add(Control(DesignerControlTypes.TextBox, "CustomerNameTextBox", 54, 92, 260, 38, placeholder: "Name", background: "#FFFFFF", foreground: "#0F172A"));
        vm.Controls.Add(Control(DesignerControlTypes.Button, "SaveButton", 54, 146, 130, 38, text: "Save", background: "#2563EB", foreground: "#FFFFFF", border: "#1D4ED8", radius: 8));
        vm.Controls.Add(Control(DesignerControlTypes.Border, "BackgroundPanel", 24, 28, 520, 210, background: "#EFF6FF", border: "#93C5FD", radius: 16));
    }

    private static void AssertPreviewAndExportUseSameControlOrder(SmokeContext context)
    {
        var comparison = context.ViewModel.ComparePreviewAndExportForActiveDocument("PreviewAndExportUseSameControlOrder");
        RequireNoPreviewExportMismatches(comparison);
        RequireControlOrder(comparison.PreviewControls, "BackgroundPanel", "CardTitle", "CustomerNameTextBox", "SaveButton");
        RequireControlOrder(comparison.ExportControls, "BackgroundPanel", "CardTitle", "CustomerNameTextBox", "SaveButton");
    }

    private static void AssertBorderDoesNotCoverTextControlsAfterExport(SmokeContext context)
    {
        var comparison = context.ViewModel.ComparePreviewAndExportForActiveDocument("BorderDoesNotCoverTextControlsAfterExport");
        RequireNoPreviewExportMismatches(comparison);

        var background = comparison.ExportControls.Single(control => control.Name == "BackgroundPanel");
        var title = comparison.ExportControls.Single(control => control.Name == "CardTitle");
        var input = comparison.ExportControls.Single(control => control.Name == "CustomerNameTextBox");
        if (background.VisualIndex >= title.VisualIndex || background.VisualIndex >= input.VisualIndex)
            throw new InvalidOperationException("Background Border should be below text controls in export snapshot.");
        if (background.ZIndex >= title.ZIndex || background.ZIndex >= input.ZIndex)
            throw new InvalidOperationException("Background Border should have lower ZIndex than text controls.");

        var borderIndex = context.Xaml.IndexOf("x:Name=\"BackgroundPanel\"", StringComparison.Ordinal);
        var titleIndex = context.Xaml.IndexOf("x:Name=\"CardTitle\"", StringComparison.Ordinal);
        var inputIndex = context.Xaml.IndexOf("x:Name=\"CustomerNameTextBox\"", StringComparison.Ordinal);
        if (borderIndex < 0 || titleIndex < 0 || inputIndex < 0 || borderIndex > titleIndex || borderIndex > inputIndex)
            throw new InvalidOperationException("Exported AXAML order should keep background Border before foreground text controls.");
    }

    private static void ConfigurePreviewExportBoundsForm(MainWindowViewModel vm)
    {
        vm.DesignWidth = 1200;
        vm.DesignHeight = 800;
        vm.SurfaceLayoutMode = DesignerLayoutModes.Absolute;
        vm.Controls.Add(Control(DesignerControlTypes.TextBlock, "TitleText", 32, 24, 260, 30, text: "Bounds"));
        vm.Controls.Add(Control(DesignerControlTypes.TextBox, "InputBox", 132, 104, 320, 42, placeholder: "Name"));
        vm.Controls.Add(Control(DesignerControlTypes.Border, "DetailsCard", 96, 176, 420, 140, background: "#F8FAFC", border: "#CBD5E1", radius: 12));
    }

    private static void AssertPreviewAndExportHaveSameBounds(SmokeContext context)
    {
        var comparison = context.ViewModel.ComparePreviewAndExportForActiveDocument("PreviewAndExportHaveSameBounds");
        RequireNoPreviewExportMismatches(comparison);
        foreach (var preview in comparison.PreviewControls)
        {
            var export = comparison.ExportControls.Single(control => control.ControlId == preview.ControlId);
            RequireSameBounds(preview, export);
        }
    }

    private static void ConfigurePreviewExportPropertiesForm(MainWindowViewModel vm)
    {
        vm.SurfaceLayoutMode = DesignerLayoutModes.Absolute;
        var button = Control(DesignerControlTypes.Button, "StyledButton", 40, 46, 180, 44, text: "Styled", background: "#334155", foreground: "#F8FAFC", border: "#0F172A", radius: 11);
        button.BorderThickness = 2;
        button.Opacity = 0.82;
        button.Padding = 10;
        button.FontSize = 16;
        button.FontWeight = "Bold";
        button.FontFamily = "Inter";
        vm.Controls.Add(button);

        var text = Control(DesignerControlTypes.TextBlock, "StyledText", 40, 112, 280, 40, text: "Preview equals export", background: "Transparent", foreground: "#7C2D12", border: "#94A3B8", radius: 0);
        text.FontSize = 15;
        text.FontWeight = "SemiBold";
        vm.Controls.Add(text);
    }

    private static void AssertPreviewAndExportHaveSameKeyProperties(SmokeContext context)
    {
        var comparison = context.ViewModel.ComparePreviewAndExportForActiveDocument("PreviewAndExportHaveSameKeyProperties");
        RequireNoPreviewExportMismatches(comparison);
        var exportButton = comparison.ExportControls.Single(control => control.Name == "StyledButton");
        if (exportButton.Background != "#334155" || exportButton.Foreground != "#F8FAFC" || exportButton.BorderBrush != "#0F172A")
            throw new InvalidOperationException("Export snapshot should preserve key styled Button colors.");
        RequireContains(context.Xaml, "Background=\"#334155\"", "Styled Button background should be in AXAML.");
        RequireContains(context.Xaml, "Foreground=\"#F8FAFC\"", "Styled Button foreground should be in AXAML.");
    }

    private static void ConfigurePreviewExportStateMutationForm(MainWindowViewModel vm)
    {
        vm.Controls.Add(Control(DesignerControlTypes.Button, "SelectedButton", 40, 40, 160, 40, text: "Selected"));
        var button = vm.Controls.Single(control => control.Name == "SelectedButton");
        vm.SelectControls(new[] { button }, button);
        vm.MarkCanvasRenderedDocument("PreviewExportStateMutationForm", vm.Controls.Count, 0);
    }

    private static void AssertPreviewGenerationDoesNotMutateEditorState(SmokeContext context)
    {
        var before = CaptureEditorState(context.ViewModel);
        var comparison = context.ViewModel.ComparePreviewAndExportForActiveDocument("PreviewGenerationDoesNotMutateEditorState");
        RequireNoPreviewExportMismatches(comparison);
        var after = CaptureEditorState(context.ViewModel);
        RequireEditorStateUnchanged(before, after, "Preview/export comparison should not mutate editor state.");
    }

    private static void AssertAxamlPreviewSettingsAreAvailable(SmokeContext context)
    {
        var defaults = new AppSettingsModel();
        if (defaults.Preview.UseExportedAxamlPreview)
            throw new InvalidOperationException("UseExportedAxamlPreview should default to false.");
        if (!defaults.Preview.FallbackToLegacyPreviewOnAxamlError)
            throw new InvalidOperationException("AXAML Preview fallback should default to true.");

        var settings = new SettingsWindowViewModel(context.ViewModel, Path.Combine(Path.GetTempPath(), "app-settings.json"));
        settings.UseExportedAxamlPreview = true;
        settings.FallbackToLegacyPreviewOnAxamlError = false;
        settings.ShowGeneratedAxamlOnPreviewError = true;
        settings.ApplyCommand.Execute(null);

        if (!context.ViewModel.UseExportedAxamlPreview)
            throw new InvalidOperationException("Settings Window should apply UseExportedAxamlPreview to the main ViewModel.");
        if (context.ViewModel.FallbackToLegacyPreviewOnAxamlError)
            throw new InvalidOperationException("Settings Window should apply AXAML Preview fallback setting.");

        var xaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "SettingsWindow.axaml"), Encoding.UTF8);
        RequireContains(xaml, "UseExportedAxamlPreview", "Settings Preview page should expose UseExportedAxamlPreview.");
        RequireContains(SettingsTextCatalog.Russian.UseExportedAxamlPreview, "AXAML", "Russian settings catalog should describe AXAML Preview.");
        RequireContains(SettingsTextCatalog.English.UseExportedAxamlPreview, "AXAML", "English settings catalog should describe AXAML Preview.");
    }

    private static void AssertAxamlPreviewUsesExportGenerator(SmokeContext context)
    {
        var payload = context.ViewModel.GenerateExportedAxamlPreviewSnapshot();
        RequireContains(payload.Axaml, "<Button", "AXAML Preview payload should include controls from the export generator.");
        RequireContains(payload.ExportAxaml, "x:Class=", "Export AXAML should retain its compiled x:Class.");
        RequireNotContains(payload.Axaml, "AvaloniaApplication1.MainWindow", "Runtime Preview AXAML should not reference the exported code-behind class.");
        RequireContains(payload.Axaml, "<UserControl", "Runtime Preview AXAML should use a safe UserControl root.");
        RequireNotContains(payload.Axaml, "x:Class=", "Runtime Preview AXAML should not require a compiled XAML class directive.");
        RequireEqual(context.Xaml, payload.ExportAxaml, "AXAML Preview should use the same AXAML generator output as Export.");

        var viewModelSource = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "ViewModels", "MainWindowViewModel.cs"), Encoding.UTF8);
        RequireContains(viewModelSource, "GenerateExportedAxamlPreviewSnapshot", "MainWindowViewModel should expose an AXAML Preview snapshot method.");
        RequireContains(viewModelSource, "exportViewModel.GenerateXaml()", "AXAML Preview snapshot should call the existing Export AXAML generator on an isolated VM.");
        RequireContains(viewModelSource, "AxamlGenerationMode.RuntimePreview", "AXAML Preview should explicitly request the RuntimePreview AXAML mode.");
        RequireContains(viewModelSource, "AXAML_PREVIEW_GENERATION_START", "AXAML Preview generation diagnostics should be emitted.");
        RequireContains(viewModelSource, "AXAML_PREVIEW_GENERATION_END", "AXAML Preview generation completion diagnostics should be emitted.");
        RequireContains(viewModelSource, "AXAML_PREVIEW_ROOT_TRANSFORM_START", "AXAML Preview root transformation start should be diagnosed.");
        RequireContains(viewModelSource, "AXAML_PREVIEW_ROOT_PROPERTY_REMOVED", "Removed Window-only root properties should be diagnosed.");
        RequireContains(viewModelSource, "AXAML_PREVIEW_ROOT_TRANSFORM_END", "AXAML Preview root transformation completion should be diagnosed.");
    }

    private static void AssertAxamlPreviewDoesNotMutateEditorState(SmokeContext context)
    {
        context.ViewModel.UseExportedAxamlPreview = true;
        context.ViewModel.InteractionTraceEntries.Clear();
        var before = CaptureEditorState(context.ViewModel);
        var beforeXaml = context.ViewModel.GeneratedXaml;
        var beforeCSharp = context.ViewModel.GeneratedCSharp;

        var payload = context.ViewModel.GenerateExportedAxamlPreviewSnapshot();

        var after = CaptureEditorState(context.ViewModel);
        RequireEditorStateUnchanged(before, after, "AXAML Preview snapshot generation should not mutate editor state.");
        RequireEqual(beforeXaml, context.ViewModel.GeneratedXaml, "AXAML Preview snapshot generation should not overwrite current GeneratedXaml.");
        RequireEqual(beforeCSharp, context.ViewModel.GeneratedCSharp, "AXAML Preview snapshot generation should not overwrite current GeneratedCSharp.");
        RequireContains(payload.Axaml, "<Button", "AXAML Preview snapshot should still produce usable AXAML.");
        RequireNoTraceEvent(context, "AXAML_PREVIEW_EDITOR_STATE_MUTATION_DETECTED");
    }

    private static void AssertAxamlPreviewLoaderUsesAvaloniaXamlLoader(SmokeContext context)
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "PreviewWindow.axaml.cs"), Encoding.UTF8);
        RequireContains(source, "RuntimeAxamlPreviewLoader.Load(preview.Axaml,", "AXAML Preview should load generated AXAML through the in-memory runtime loader.");
        RequireContains(source, "AXAML_PREVIEW_LOAD_START", "AXAML Preview load diagnostics should be emitted.");
        RequireContains(source, "AXAML_PREVIEW_LOAD_SUCCESS", "AXAML Preview success diagnostics should be emitted.");
        RequireContains(source, "AXAML_PREVIEW_LOAD_FAILED", "AXAML Preview failure diagnostics should be emitted.");
        RequireContains(source, "AXAML_PREVIEW_FALLBACK_TO_LEGACY", "AXAML Preview fallback diagnostics should be emitted.");
    }

    private static void AssertRuntimePreviewDoesNotUsePrecompiledResourceLoaderForTempFile(SmokeContext context)
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "PreviewWindow.axaml.cs"), Encoding.UTF8);
        RequireNotContains(source, "AvaloniaXamlLoader.Load(new Uri", "Runtime Preview must not use the precompiled-resource URI loader.");
        RequireNotContains(source, "WriteTemporaryPreviewAxaml", "Runtime Preview must not write generated AXAML to a temporary resource file.");
        RequireNotContains(source, "Path.GetTempPath()", "Runtime Preview must not construct temporary preview AXAML paths.");
        RequireContains(source, "mode=runtime-string", "Runtime Preview diagnostics should identify the in-memory load mode.");
    }

    private static void AssertRuntimePreviewAxamlHasNoExportXClass(SmokeContext context)
    {
        var payload = context.ViewModel.GenerateExportedAxamlPreviewSnapshot();
        RequireContains(payload.ExportAxaml, "x:Class=", "Export AXAML must keep x:Class.");
        RequireNotContains(payload.Axaml, "AvaloniaApplication1.MainWindow", "Runtime Preview AXAML must remove the exported x:Class.");
        RequireContains(payload.Axaml, "<UserControl", "Runtime Preview AXAML should use the safe UserControl root.");
        RequireNotContains(payload.Axaml, "x:Class=", "Runtime Preview AXAML should be independent of compiled XAML class directives.");
        RequireContains(payload.Axaml, "<Button", "Removing compiled XAML directives must preserve the exported control tree.");
        if (string.IsNullOrWhiteSpace(payload.RemovedXClass))
            throw new InvalidOperationException("Runtime Preview payload should record the removed export x:Class for diagnostics.");
    }

    private static void AssertRuntimePreviewUserControlDoesNotContainWindowOnlyProperties(SmokeContext context)
    {
        const string exportAxaml = @"<Window xmlns=""https://github.com/avaloniaui""
                         xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
                         x:Class=""Smoke.Window""
                         Title=""Smoke""
                         RequestedThemeVariant=""Light""
                         Icon=""smoke.ico""
                         WindowState=""Normal""
                         WindowStartupLocation=""CenterScreen""
                         CanResize=""True""
                         CanMinimize=""True""
                         CanMaximize=""True""
                         ShowInTaskbar=""True""
                         ShowActivated=""True""
                         Topmost=""False""
                         SystemDecorations=""Full""
                         SizeToContent=""Manual""
                         Position=""0,0""
                         ClientSize=""640,480""
                         TransparencyLevelHint=""None""
                         TransparencyBackgroundFallback=""#FFFFFF""
                         OffScreenMargin=""0""
                         ExtendClientAreaToDecorationsHint=""False""
                         ExtendClientAreaChromeHints=""Default""
                         ExtendClientAreaTitleBarHeightHint=""0""
                         Width=""640""
                         Height=""480""
                         Background=""#FFFFFF"">
              <Window.Resources>
                <SolidColorBrush x:Key=""PreviewBackground"">#FFFFFF</SolidColorBrush>
              </Window.Resources>
              <Window.Styles>
                <Style Selector=""TextBox"">
                  <Setter Property=""Foreground"" Value=""#000000"" />
                </Style>
              </Window.Styles>
              <Canvas />
            </Window>";

        var preview = GeneratedAxamlService.Create(exportAxaml, AxamlGenerationMode.RuntimePreview);
        var windowOnlyProperties = new[]
        {
            "Title",
            "RequestedThemeVariant",
            "Icon",
            "WindowState",
            "WindowStartupLocation",
            "CanResize",
            "CanMinimize",
            "CanMaximize",
            "ShowInTaskbar",
            "ShowActivated",
            "Topmost",
            "SystemDecorations",
            "SizeToContent",
            "Position",
            "ClientSize",
            "TransparencyLevelHint",
            "TransparencyBackgroundFallback",
            "OffScreenMargin",
            "ExtendClientAreaToDecorationsHint",
            "ExtendClientAreaChromeHints",
            "ExtendClientAreaTitleBarHeightHint"
        };

        RequireContains(preview.Axaml, "<UserControl", "Runtime Preview should replace the Window root with UserControl.");
        RequireContains(preview.Axaml, "Width=\"640\"", "Runtime Preview should preserve Control-compatible Width.");
        RequireContains(preview.Axaml, "Height=\"480\"", "Runtime Preview should preserve Control-compatible Height.");
        RequireContains(preview.Axaml, "Background=\"#FFFFFF\"", "Runtime Preview should preserve Control-compatible Background.");
        RequireContains(preview.Axaml, "<UserControl.Resources>", "Runtime Preview should retarget Window.Resources to UserControl.Resources.");
        RequireContains(preview.Axaml, "<UserControl.Styles>", "Runtime Preview should retarget Window.Styles to UserControl.Styles.");
        RequireNotContains(preview.Axaml, "<Window.Resources>", "Runtime Preview must not retain a Window-owned Resources property element.");
        RequireNotContains(preview.Axaml, "<Window.Styles>", "Runtime Preview must not retain a Window-owned Styles property element.");
        RequireEqual(2, preview.NormalizedRootPropertyElementCount, "Runtime Preview should report normalized root property elements.");
        foreach (var propertyName in windowOnlyProperties)
        {
            RequireNotContains(
                preview.Axaml,
                propertyName + "=\"",
                $"Runtime Preview UserControl must not contain Window-only property {propertyName}.");
            if (!preview.RemovedRootProperties.Contains(propertyName, StringComparer.Ordinal))
                throw new InvalidOperationException($"Runtime Preview transformation should report removed root property {propertyName}.");
        }
    }

    private static void AssertExportWindowStillContainsWindowProperties(SmokeContext context)
    {
        var payload = context.ViewModel.GenerateExportedAxamlPreviewSnapshot();
        var requiredWindowProperties = new[]
        {
            "Title",
            "RequestedThemeVariant",
            "WindowState",
            "WindowStartupLocation",
            "CanResize",
            "ShowInTaskbar",
            "Topmost",
            "SystemDecorations"
        };

        RequireContains(payload.ExportAxaml, "<Window", "Export mode should retain the Window root.");
        foreach (var propertyName in requiredWindowProperties)
            RequireContains(payload.ExportAxaml, propertyName + "=\"", $"Export mode should retain Window property {propertyName}.");
    }

    private static void AssertRuntimePreviewThemeAppliedByHost(SmokeContext context)
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "PreviewWindow.axaml.cs"), Encoding.UTF8);
        RequireContains(source, "RequestedThemeVariant = ResolveThemeVariant(_document.FormTheme)", "PreviewWindow should apply the document theme to the Window host.");
        RequireContains(source, "AXAML_PREVIEW_THEME_APPLIED_BY_HOST", "Runtime Preview should diagnose that theme selection belongs to the Window host.");

        var payload = context.ViewModel.GenerateExportedAxamlPreviewSnapshot();
        RequireNotContains(payload.Axaml, "RequestedThemeVariant=\"", "Runtime Preview UserControl must not receive RequestedThemeVariant.");
    }

    private static void AssertRuntimePreviewLoadsAxamlFromStringOrStream(SmokeContext context)
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Services", "RuntimeAxamlPreviewLoader.cs"), Encoding.UTF8);
        RequireContains(source, "new RuntimeXamlLoaderDocument(new Uri(SyntheticBaseUri), axaml)", "Runtime Preview should create an in-memory runtime XAML document with a stable base URI.");
        RequireContains(source, "AvaloniaRuntimeXamlLoader.Load(document, configuration)", "Runtime Preview should load the in-memory runtime XAML document.");
        RequireContains(source, "SyntheticBaseUri", "Runtime Preview should use a stable synthetic base URI.");
        RequireNotContains(source, "File.", "The runtime loader service must not read generated AXAML from a file.");
    }

    private static void AssertRuntimePreviewCanLoadSimpleWindow(SmokeContext context)
    {
        EnsureAvaloniaRuntimeInitialized();
        try
        {
            RuntimeAxamlPreviewLoader.Load(@"<Window xmlns=""https://github.com/avaloniaui""><Button Content=""Direct"" /></Window>");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Official Window/Button runtime loader baseline failed.", ex);
        }
        const string exportAxaml = @"<Window xmlns=""https://github.com/avaloniaui""
                         xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
                         x:Class=""Smoke.GeneratedWindow""
                         Title=""Runtime preview""
                         RequestedThemeVariant=""Light""
                         WindowState=""Normal""
                         WindowStartupLocation=""CenterScreen""
                         CanResize=""True""
                         ShowInTaskbar=""True""
                         Topmost=""False""
                         SystemDecorations=""Full"">
              <Window.Resources>
                <SolidColorBrush x:Key=""RuntimePreviewBackground"">#FFFFFF</SolidColorBrush>
              </Window.Resources>
              <Window.Styles>
                <Style Selector=""TextBox"">
                  <Setter Property=""Foreground"" Value=""#000000"" />
                </Style>
              </Window.Styles>
              <Canvas Width=""640"" Height=""480"">
                <TextBox x:Name=""RuntimeTextBox"" Text=""Runtime AXAML"" />
                <TextBlock Text=""Runtime text"" />
                <Button Content=""Runtime button"" />
              </Canvas>
            </Window>";

        var axaml = GeneratedAxamlService.Create(exportAxaml, AxamlGenerationMode.RuntimePreview).Axaml;
        var loaded = RuntimeAxamlPreviewLoader.Load(axaml);
        if (loaded is not UserControl { Content: Canvas canvas })
            throw new InvalidOperationException($"Runtime AXAML loader returned {loaded.GetType().FullName} instead of the safe Runtime Preview root.");
        if (canvas.Children.OfType<TextBox>().SingleOrDefault()?.Text != "Runtime AXAML")
            throw new InvalidOperationException("Runtime AXAML loader did not create the expected TextBox visual tree.");
        if (canvas.Children.OfType<TextBlock>().SingleOrDefault()?.Text != "Runtime text")
            throw new InvalidOperationException("Runtime AXAML loader did not create the expected TextBlock visual tree.");
        if (!Equals(canvas.Children.OfType<Button>().SingleOrDefault()?.Content, "Runtime button"))
            throw new InvalidOperationException("Runtime AXAML loader did not create the expected Button visual tree.");
    }

    private static void AssertRuntimePreviewCanLoadDataGrid(SmokeContext context)
    {
        EnsureAvaloniaRuntimeInitialized();
        var payload = context.ViewModel.GenerateExportedAxamlPreviewSnapshot();
        var loaded = RuntimeAxamlPreviewLoader.Load(payload.Axaml);
        if (loaded is not UserControl root)
            throw new InvalidOperationException($"Runtime AXAML loader returned {loaded.GetType().FullName} instead of the transformed UserControl root.");

        var grid = root.GetLogicalDescendants().OfType<DataGrid>().SingleOrDefault()
                   ?? throw new InvalidOperationException("Runtime AXAML Preview did not create the exported DataGrid visual.");

        grid.ItemsSource = new[] { new RuntimePreviewSmokeRow { Name = "Real row" } };
        if (grid.Columns.Count == 0)
            throw new InvalidOperationException("Runtime AXAML DataGrid did not retain its exported columns.");
        if (grid.ItemsSource?.Cast<object>().Count() != 1)
            throw new InvalidOperationException("Runtime AXAML DataGrid did not accept its Preview ItemsSource.");
    }

    private static void EnsureAvaloniaRuntimeInitialized()
    {
        if (_avaloniaRuntimeInitialized || Application.Current is not null)
        {
            _avaloniaRuntimeInitialized = true;
            return;
        }

        AppBuilder.Configure<FormDesigner.App>()
            .UsePlatformDetect()
            .WithInterFont()
            .SetupWithoutStarting();
        _avaloniaRuntimeInitialized = true;
    }

    private static void AssertRuntimePreviewFailureShowsUsefulDiagnostics(SmokeContext context)
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "PreviewWindow.axaml.cs"), Encoding.UTF8);
        RequireContains(source, "AXAML_PREVIEW_RUNTIME_LOAD_FAILED", "Runtime AXAML failures should emit a dedicated diagnostic event.");
        RequireContains(source, "GetXamlErrorLocation", "Runtime AXAML failures should extract line and position information.");
        RequireContains(source, "loaderType=", "Runtime AXAML failure diagnostics should include the loader type.");
        RequireContains(source, "baseUri=", "Runtime AXAML failure diagnostics should include the base URI.");
        RequireContains(source, "AXAML_PREVIEW_FALLBACK_TO_LEGACY", "Runtime AXAML failure should preserve the legacy Preview fallback.");
    }

    private static void AssertExportGenerationDoesNotMutateEditorState(SmokeContext context)
    {
        context.ViewModel.InteractionTraceEntries.Clear();
        var before = CaptureEditorState(context.ViewModel);
        context.ViewModel.RefreshExportPipelineForSmokeTest();
        var after = CaptureEditorState(context.ViewModel);
        RequireEditorStateUnchanged(before, after, "Export pipeline should remain read-only for editor state.");
        RequireNoExportEditorMutationTrace(context.ViewModel);
        RequireNoApplyDocumentTrace(context.ViewModel, "ExportGenerationDoesNotMutateEditorState");
    }

    private static void ConfigureResizeFormDoesNotMoveCanvasControls(MainWindowViewModel vm)
    {
        vm.SurfaceLayoutMode = DesignerLayoutModes.Absolute;
        vm.DesignWidth = 1200;
        vm.DesignHeight = 800;
        vm.Controls.Add(Control(DesignerControlTypes.Button, "ResizeButton", 100, 100, 160, 40, text: "Resize"));
        vm.Controls.Add(Control(DesignerControlTypes.TextBox, "ResizeInput", 260, 180, 240, 38, placeholder: "Input"));
    }

    private static void AssertResizeFormDoesNotMoveCanvasControls(SmokeContext context)
    {
        var before = CaptureBoundsByName(context.ViewModel);
        context.ViewModel.DesignWidth = 780;
        context.ViewModel.DesignHeight = 520;
        var after = CaptureBoundsByName(context.ViewModel);
        RequireBoundsMapUnchanged(before, after, "Changing form size must not move Canvas controls.");
        RequireContains(context.Xaml, "Canvas.Left=\"100\" Canvas.Top=\"100\"", "Original Button position should remain exported.");
    }

    private static void ConfigureResizePreviewWindowDoesNotChangeControlPositions(MainWindowViewModel vm)
    {
        vm.SurfaceLayoutMode = DesignerLayoutModes.Absolute;
        vm.DesignWidth = 1200;
        vm.DesignHeight = 800;
        vm.Controls.Add(Control(DesignerControlTypes.Button, "PreviewResizeButton", 100, 100, 160, 40, text: "Preview"));
        vm.Controls.Add(Control(DesignerControlTypes.Border, "PreviewResizePanel", 380, 220, 280, 120, background: "#F8FAFC", border: "#CBD5E1", radius: 12));
    }

    private static void AssertResizePreviewWindowDoesNotChangeControlPositions(SmokeContext context)
    {
        var compact = context.ViewModel.BuildPreviewControlSnapshotsForActiveDocument("PreviewWindowCompact", 900, 600);
        var wide = context.ViewModel.BuildPreviewControlSnapshotsForActiveDocument("PreviewWindowWide", 1600, 1000);
        foreach (var compactControl in compact)
        {
            var wideControl = wide.Single(control => control.ControlId == compactControl.ControlId);
            RequireSameBounds(compactControl, wideControl);
        }
    }

    private static void ConfigureButtonCornerRadiusPreviewExportMatch(MainWindowViewModel vm)
    {
        vm.SurfaceLayoutMode = DesignerLayoutModes.Absolute;
        var button = Control(DesignerControlTypes.Button, "RoundButton", 80, 90, 180, 44, text: "Round", background: "#2563EB", foreground: "#FFFFFF", border: "#1D4ED8", radius: 18);
        vm.Controls.Add(button);
    }

    private static void AssertButtonCornerRadiusPreviewExportMatch(SmokeContext context)
    {
        var comparison = context.ViewModel.ComparePreviewAndExportForActiveDocument("ButtonCornerRadiusPreviewExportMatch");
        RequireNoPreviewExportMismatches(comparison);
        var exportButton = comparison.ExportControls.Single(control => control.Name == "RoundButton");
        if (Math.Abs(exportButton.CornerRadius - 18) > 0.01)
            throw new InvalidOperationException("Button CornerRadius should be preserved in export snapshot.");
        RequireContains(context.Xaml, "CornerRadius=\"18\"", "Button CornerRadius should be exported in AXAML.");
    }

    private static void ConfigureOpacityZeroDesignerOnlyOutline(MainWindowViewModel vm)
    {
        vm.SurfaceLayoutMode = DesignerLayoutModes.Absolute;
        var hidden = Control(DesignerControlTypes.Button, "InvisibleButton", 120, 130, 180, 44, text: "Invisible", background: "#2563EB", foreground: "#FFFFFF", border: "#1D4ED8", radius: 8);
        hidden.Opacity = 0;
        vm.Controls.Add(hidden);
        vm.SelectControls(new[] { hidden }, hidden);
    }

    private static void AssertOpacityZeroDesignerOnlyOutline(SmokeContext context)
    {
        if (context.ViewModel.SelectedControl?.Name != "InvisibleButton")
            throw new InvalidOperationException("Opacity=0 control should remain selectable in designer model.");
        var comparison = context.ViewModel.ComparePreviewAndExportForActiveDocument("OpacityZeroDesignerOnlyOutline");
        RequireNoPreviewExportMismatches(comparison);
        var exportControl = comparison.ExportControls.Single(control => control.Name == "InvisibleButton");
        if (Math.Abs(exportControl.Opacity) > 0.01)
            throw new InvalidOperationException("Opacity=0 should be preserved for preview/export runtime behavior.");
        RequireContains(context.Xaml, "Opacity=\"0\"", "Opacity=0 should be exported.");
        RequireNotContains(context.Xaml, "DESIGNER_INVISIBLE_CONTROL_OUTLINE_SHOWN", "Designer-only invisible outline must not be exported.");
        RequireNotContains(context.Xaml, "StrokeDashArray", "Designer-only invisible outline must not be exported.");
    }

    private static void ConfigurePreviewExportBoundsMatchAfterResize(MainWindowViewModel vm)
    {
        ConfigureResizeFormDoesNotMoveCanvasControls(vm);
    }

    private static void AssertPreviewExportBoundsMatchAfterResize(SmokeContext context)
    {
        context.ViewModel.DesignWidth = 1500;
        context.ViewModel.DesignHeight = 920;
        var comparison = context.ViewModel.ComparePreviewAndExportForActiveDocument("PreviewExportBoundsMatchAfterResize");
        RequireNoPreviewExportMismatches(comparison);
    }

    private static void ConfigureRealDataGridExport(MainWindowViewModel vm)
    {
        var source = ProductsSource();
        vm.BindingSources.Add(source);
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        vm.Controls.Add(DataGrid("ProductsGrid", source.Id, 32, 42, 720, 360));
    }

    private static void AssertGeneratedFormDesignSystemAppliesSemanticControlStates(SmokeContext context)
    {
        RequireContains(context.Xaml, "ThemeAccentHoverBrush", "Generated forms should expose a semantic accent hover token.");
        RequireContains(context.Xaml, "ThemeAccentPressedBrush", "Generated forms should expose a semantic accent pressed token.");
        RequireContains(context.Xaml, "ThemeAccentSubtleBrush", "Generated forms should expose an alpha accent surface token.");
        RequireContains(context.Xaml, "Color=\"#18", "Light theme subtle accents should use alpha instead of a hard-coded opaque pastel.");
        RequireContains(context.Xaml, "<Style Selector=\"Button:pointerover\">", "Button hover styling should be generated.");
        RequireContains(context.Xaml, "<Style Selector=\"Button:pressed\">", "Button pressed styling should be generated.");
        RequireContains(context.Xaml, "<Style Selector=\"Button:focus\">", "Button focus styling should be generated.");
        RequireContains(context.Xaml, "<Style Selector=\"Button:disabled\">", "Button disabled styling should be generated.");
        RequireContains(context.Xaml, "<Style Selector=\"TextBox:focus\">", "TextBox focus styling should be generated.");
        RequireContains(context.Xaml, "<Style Selector=\"ComboBoxItem:selected\">", "ComboBox selection styling should be generated for future standard controls.");
        RequireContains(context.Xaml, "<Style Selector=\"RadioButton:pointerover\">", "RadioButton hover styling should be generated for future standard controls.");
        RequireContains(context.Xaml, "<Style Selector=\"ListBoxItem:selected\">", "ListBox selection styling should be generated for future standard controls.");
        RequireContains(context.Xaml, "<Style Selector=\"TreeViewItem:selected\">", "TreeView selection styling should be generated for future standard controls.");
        RequireContains(context.Xaml, "<Style Selector=\"TabItem:selected\">", "Tab selection styling should be generated for future standard controls.");
        RequireContains(context.Xaml, "<Style Selector=\"MenuItem:selected\">", "Menu selection styling should be generated for future standard controls.");
        RequireTraceEvent(context, "EXPORT_FORM_DESIGN_SYSTEM_GENERATED");

        var legacyPreviewSource = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "PreviewWindow.axaml.cs"), Encoding.UTF8);
        RequireContains(legacyPreviewSource, "GeneratedFormDesignSystem.CreateTokens", "Legacy Preview should resolve default colours through the same design-system token generator.");

        EnsureAvaloniaRuntimeInitialized();
        var preview = context.ViewModel.GenerateExportedAxamlPreviewSnapshot();
        if (RuntimeAxamlPreviewLoader.Load(preview.Axaml) is not UserControl)
            throw new InvalidOperationException("Runtime AXAML Preview should load the shared generated-form style system.");
    }

    private static void AssertGeneratedFormDesignSystemStylesDataGridAndRuntimePreview(SmokeContext context)
    {
        RequireContains(context.Xaml, "ThemeDataGridHoverRowBackgroundBrush", "DataGrid hover colour should come from the shared semantic tokens.");
        RequireContains(context.Xaml, "ThemeDataGridSelectedRowBackgroundBrush", "DataGrid selection colour should come from the shared semantic tokens.");
        RequireContains(context.Xaml, "ThemeDataGridGridLineBrush", "DataGrid grid lines should use the shared semantic tokens.");
        RequireContains(context.Xaml, "<Style Selector=\"DataGridColumnHeader:pointerover\">", "DataGrid header hover styling should be generated.");
        RequireContains(context.Xaml, "<Style Selector=\"DataGridCell:selected\">", "DataGrid selected-cell styling should be generated.");
        RequireContains(context.Xaml, "Value=\"{StaticResource ThemeDataGridHoverRowBackgroundBrush}\"", "Per-grid hover styling should inherit the token when the user has not overridden it.");
        RequireContains(context.Xaml, "Value=\"{StaticResource ThemeDataGridSelectedRowBackgroundBrush}\"", "Per-grid selection styling should inherit the token when the user has not overridden it.");

        EnsureAvaloniaRuntimeInitialized();
        var preview = context.ViewModel.GenerateExportedAxamlPreviewSnapshot();
        var root = RuntimeAxamlPreviewLoader.Load(preview.Axaml) as UserControl
                   ?? throw new InvalidOperationException("Runtime AXAML Preview should load a styled DataGrid as UserControl.");
        if (root.GetLogicalDescendants().OfType<DataGrid>().SingleOrDefault() is null)
            throw new InvalidOperationException("Runtime AXAML Preview should retain the styled generated DataGrid.");
    }

    private static void ConfigureRealDataGridExportWithDemo(MainWindowViewModel vm)
    {
        ConfigureRealDataGridExport(vm);
        vm.BindingSources.Single().UseDemoData = true;
    }

    private static void AssertRealDataGridExport(SmokeContext context)
    {
        RequireContains(context.Xaml, "<DataGrid", "Generated XAML should use real Avalonia DataGrid.");
        RequireContains(context.Xaml, "<DataGrid.Columns>", "Generated XAML should define real DataGrid columns.");
        RequireContains(context.Xaml, "Title", "Generated XAML should contain BindingSource field Title.");
        RequireContains(context.Xaml, "Price", "Generated XAML should contain BindingSource field Price.");
        RequireContains(context.Xaml, "Count", "Generated XAML should contain BindingSource field Count.");
        RequireContains(context.Xaml, "<DataGridTextColumn Header=\"Title\" Binding=\"{Binding Title}\"", "Real DataGrid should export TextColumn with attribute binding.");
        RequireNotContains(context.Xaml, "<dataGrid:DataGridTextColumn", "Real DataGrid column tag must not use dataGrid prefix.");
        RequireContains(context.ChecklistText, "DataGrid: Real Avalonia DataGrid", "Checklist should report real DataGrid mode.");
        RequireContains(context.ChecklistText, "Avalonia.Controls.DataGrid", "Checklist should mention required DataGrid NuGet.");
        RequireContains(context.Xaml, "ItemsSource=\"{Binding ProductsView}\"", "Real DataGrid export should include generated ItemsSource binding.");
        RequireContains(context.CSharp, "public ObservableCollection<ProductRow> Products { get; } = new();", "Real DataGrid export should generate source collection.");
        RequireContains(context.CSharp, "public partial class ProductRow : ObservableObject", "Real DataGrid export should generate row DTO.");
    }

    private static void AssertRealDataGridExportUsesValidColumnTags(SmokeContext context)
    {
        RequireContains(context.Xaml, "<DataGridTextColumn", "Real DataGrid export should use unprefixed DataGridTextColumn.");
        RequireNotContains(context.Xaml, "<dataGrid:DataGridTextColumn", "Real DataGrid export must not use invalid prefixed DataGridTextColumn.");
        RequireNotContains(context.Xaml, "DataGridTextColumn.Binding", "Real DataGrid export should use simple attribute bindings.");
        RequireNotContains(context.Xaml, "HeaderTemplate", "Real DataGrid export should not generate HeaderTemplate for simple text headers.");
    }

    private static void AssertDataGridExportGeneratesItemsSourceProperty(SmokeContext context)
    {
        RequireContains(context.Xaml, "ItemsSource=\"{Binding ProductsView}\"", "Manual/schema DataGrid should bind to generated ViewModel collection.");
        RequireContains(context.Xaml, "SelectedItem=\"{Binding SelectedProductRow, Mode=TwoWay}\"", "DataGrid should bind SelectedItem to generated ViewModel state.");
        RequireContains(context.CSharp, "public ObservableCollection<ProductRow> Products { get; } = new();", "Generated ViewModel should expose source collection.");
        RequireContains(context.CSharp, "public ObservableCollection<ProductRow> ProductsView { get; } = new();", "Generated ViewModel should expose filtered/sorted view collection.");
        RequireContains(GetRequiredPackagesText(context), "CommunityToolkit.Mvvm", "Generated runtime DataGrid binding should include CommunityToolkit package.");
    }

    private static void AssertDataGridExportGeneratesRowDto(SmokeContext context)
    {
        RequireContains(context.CSharp, "public partial class ProductRow : ObservableObject", "Generated project should include row DTO.");
        RequireContains(context.CSharp, "private string title;", "DTO should include string field from DataGrid schema.");
        RequireContains(context.CSharp, "private decimal price;", "DTO should include decimal field from DataGrid schema.");
        RequireContains(context.CSharp, "private int count;", "DTO should include int field from DataGrid schema.");
    }

    private static void AssertDataGridExportGeneratesSampleData(SmokeContext context)
    {
        RequireContains(context.CSharp, "SeedProductRow();", "Generated ViewModel constructor should seed sample data.");
        RequireContains(context.CSharp, "Products.Add(new ProductRow", "Generated sample loader should add sample rows.");
        RequireContains(context.CSharp, "Title = \"Keyboard\"", "Generated sample data should preserve field sample values.");
    }

    private static void AssertDataGridExportBuildsWithoutManualFix(SmokeContext context)
    {
        RequireContains(context.Xaml, "<DataGridTextColumn Header=\"Title\" Binding=\"{Binding Title}\"", "Generated AXAML should be build-ready without manual column fixes.");
        RequireGeneratedMainWindowViewModelAssignment(context, "Generated code-behind should attach generated ViewModel.");
        RequireNotContains(context.Xaml, "{Binding }", "Generated AXAML should not contain empty bindings.");
        RequireNotContains(context.Xaml, "x:DataType=\"\"", "Generated AXAML should not contain empty x:DataType.");
    }

    private static void AssertDataGridHeaderTemplateDoesNotGenerateEmptyBinding(SmokeContext context)
    {
        RequireContains(context.Xaml, "<DataGrid", "BindingSource workflow should export a real DataGrid.");
        RequireContains(context.Xaml, "Binding=\"{Binding Id}\"", "DataGrid should include BindingSource field Id.");
        RequireContains(context.Xaml, "Binding=\"{Binding Name}\"", "DataGrid should include BindingSource field Name.");
        RequireContains(context.Xaml, "Binding=\"{Binding Email}\"", "DataGrid should include BindingSource field Email.");
        RequireContains(context.Xaml, "Binding=\"{Binding Status}\"", "DataGrid should include BindingSource field Status.");
        RequireNotContains(context.Xaml, "{Binding }", "DataGrid export must not generate empty binding markup.");
        RequireNotContains(context.Xaml, "Path=\"\"", "DataGrid export must not generate empty binding path.");
        RequireNotContains(context.Xaml, "x:DataType=\"\"", "DataGrid export must not generate empty x:DataType.");
        RequireNotContains(context.Xaml, "DataGridTextColumn.HeaderTemplate", "DataGrid export should not generate simple HeaderTemplate bindings.");
        RequireNotContains(context.Xaml, "Text=\"{Binding}\"", "DataGrid export should not generate header TextBlock empty bindings.");
    }

    private static void AssertDataGridExportSettingsForSortResizeScroll(SmokeContext context)
    {
        RequireContains(context.Xaml, "CanUserSortColumns=\"True\"", "Real DataGrid should allow header sorting.");
        RequireContains(context.Xaml, "CanUserResizeColumns=\"True\"", "Real DataGrid should allow mouse column resize.");
        RequireContains(context.Xaml, "HorizontalScrollBarVisibility=\"Auto\"", "Real DataGrid should expose horizontal scrolling for many columns.");
        RequireContains(context.Xaml, "VerticalScrollBarVisibility=\"Auto\"", "Real DataGrid should expose vertical scrolling.");
        RequireContains(context.Xaml, "SortMemberPath=\"Title\"", "Text columns should export SortMemberPath for sorting.");
        RequireContains(context.Xaml, "CanUserResize=\"True\"", "Columns should preserve per-column resize setting.");
        RequireContains(context.Xaml, "CanUserSort=\"True\"", "Columns should preserve per-column sort setting.");
    }

    private static void AssertDataGridCellPaddingDoesNotExceedRowHeight(SmokeContext context)
    {
        RequireContains(context.Xaml, "RowHeight=\"36\"", "Baseline DataGrid export should keep fixed row height for non-wrapped cells.");
        RequireContains(context.Xaml, "FontSize=\"13\"", "Baseline DataGrid export should use row font size 13.");
        RequireContains(context.Xaml, "<Setter Property=\"Padding\" Value=\"10,4\" />", "DataGrid cell padding should preserve horizontal room without clipping row text.");
        RequireNotContains(context.Xaml, "<Setter Property=\"Padding\" Value=\"14\" />", "DataGrid export must not use uniform 14px cell/header padding with RowHeight=36.");
        RequireTraceEvent(context, "EXPORT_DATAGRID_VISUAL_STYLE_GENERATED");
        RequireTraceEvent(context, "EXPORT_DATAGRID_CELL_PADDING_ADJUSTED");
    }

    private static void AssertExportedDataGridDefaultCellPaddingIsCompact(SmokeContext context)
    {
        RequireContains(context.Xaml, "<Style Selector=\"DataGridColumnHeader\">", "DataGrid export should style column headers.");
        RequireContains(context.Xaml, "<Setter Property=\"Padding\" Value=\"10,6\" />", "DataGrid header padding should be compact vertically.");
        RequireContains(context.Xaml, "<Style Selector=\"DataGridCell\">", "DataGrid export should style cells.");
        RequireContains(context.Xaml, "<Setter Property=\"Padding\" Value=\"10,4\" />", "DataGrid cell padding should be compact vertically.");
    }

    private static void AssertExportedDataGridRowsTextNotClippedByStyle(SmokeContext context)
    {
        const double rowHeight = 36;
        const double fontSize = 13;
        const double verticalPadding = 4;
        if (rowHeight - (verticalPadding * 2) < fontSize + 4)
            throw new InvalidOperationException("Generated DataGrid cell style leaves too little vertical text area.");

        RequireContains(context.Xaml, "<Setter Property=\"MinHeight\" Value=\"36\" />", "DataGrid rows/cells should keep a 36px minimum height.");
        RequireContains(context.Xaml, "<Setter Property=\"VerticalContentAlignment\" Value=\"Center\" />", "DataGrid cells should vertically center content when supported by Avalonia.");
    }

    private static void AssertExportedDataGridDoesNotRequireInterFont(SmokeContext context)
    {
        using var export = ExportToTemporaryProject(context);
        var projectFile = Directory.GetFiles(export.Path, "*.csproj").Single();
        var programFile = Path.Combine(export.Path, "Program.cs");
        RequireFileExists(programFile, "Exported project should include Program.cs.");
        RequireContains(File.ReadAllText(projectFile, Encoding.UTF8), "Avalonia.Fonts.Inter", "Generated project should include Inter font package when exported controls request Inter.");
        RequireContains(File.ReadAllText(programFile, Encoding.UTF8), ".WithInterFont()", "Generated project should register Inter so runtime does not depend on a system-installed font.");
    }

    private static void AssertPreviewExportDataGridCellStyleMatch(SmokeContext context)
    {
        var comparison = context.ViewModel.ComparePreviewAndExportForActiveDocument("PreviewExportDataGridCellStyleMatch");
        RequireNoPreviewExportMismatches(comparison);
        RequireContains(context.Xaml, "<Setter Property=\"Padding\" Value=\"10,4\" />", "Preview/export DataGrid cell style should use compact row padding.");
    }

    private static void AssertAxamlPreviewDataGridUsesExportBinding(SmokeContext context)
    {
        var payload = context.ViewModel.GenerateExportedAxamlPreviewSnapshot();
        RequireEqual(context.Xaml, payload.ExportAxaml, "AXAML Preview DataGrid should start from the same generated AXAML as Export.");
        RequireContains(payload.Axaml, "<UserControl", "Runtime AXAML Preview should use its safe in-memory root.");
        RequireContains(payload.Axaml, "ItemsSource=\"{Binding ProductsView}\"", "AXAML Preview payload should preserve exported DataGrid ItemsSource binding.");
        RequireContains(payload.Axaml, "Binding=\"{Binding Title}\"", "AXAML Preview payload should preserve exported column binding paths.");

        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "PreviewWindow.axaml.cs"), Encoding.UTF8);
        RequireContains(source, "ParseExportedAxamlDataGridBindings", "AXAML Preview should parse DataGrid ItemsSource and column binding paths from exported AXAML.");
        RequireContains(source, "BuildExportedAxamlDataGridView", "AXAML Preview should build preview DataGrid data using exported binding paths.");
        RequireContains(source, "AXAML_PREVIEW_DATAGRID_ITEMSSOURCE_APPLIED", "AXAML Preview should log DataGrid ItemsSource application.");
    }

    private static void ConfigureDataGridColumnWrapUsesTemplateColumn(MainWindowViewModel vm)
    {
        var source = ProductsSource();
        source.Fields[0].Header = "Description";
        source.Fields[0].Path = "Description";
        source.Fields[0].SampleValue = "Long wrapped description";
        source.Fields[0].Width = "240";
        source.Fields[0].TextWrapping = BindingFieldModel.TextWrappingWrap;
        source.Fields[0].TextTrimming = BindingFieldModel.TextTrimmingNone;
        source.Fields[0].MaxLines = 0;
        vm.BindingSources.Add(source);
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        vm.Controls.Add(DataGrid("ProductsGrid", source.Id, 32, 42, 360, 220));
    }

    private static void AssertDataGridColumnWrapUsesTemplateColumn(SmokeContext context)
    {
        RequireContains(context.Xaml, "<DataGridTemplateColumn Header=\"Description\"", "Wrapped text columns should export a template column.");
        RequireContains(context.Xaml, "SortMemberPath=\"Description\"", "Wrapped template column should keep SortMemberPath.");
        RequireContains(context.Xaml, "Text=\"{Binding Description}\"", "Wrapped template column should bind cell TextBlock to the field path.");
        RequireContains(context.Xaml, "TextWrapping=\"Wrap\"", "Wrapped template column should export TextWrapping=Wrap.");
        RequireContains(context.Xaml, "TextTrimming=\"None\"", "Wrapped template column should export TextTrimming=None.");
        RequireNotContains(context.Xaml, "RowHeight=\"", "Wrapped DataGrid should not force fixed row height.");
        RequireNotContains(context.Xaml, "{Binding }", "Wrapped template column must not generate empty binding markup.");
    }

    private static void ConfigureDataGridColumnTrimmingExportedCorrectly(MainWindowViewModel vm)
    {
        var source = ProductsSource();
        source.Fields[0].TextTrimming = BindingFieldModel.TextTrimmingWordEllipsis;
        source.Fields[0].TextWrapping = BindingFieldModel.TextWrappingNoWrap;
        source.Fields[0].MaxLines = 1;
        vm.BindingSources.Add(source);
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        vm.Controls.Add(DataGrid("ProductsGrid", source.Id, 32, 42, 420, 220));
    }

    private static void AssertDataGridColumnTrimmingExportedCorrectly(SmokeContext context)
    {
        RequireContains(context.Xaml, "<DataGridTemplateColumn Header=\"Title\"", "Custom trimming should export a template column.");
        RequireContains(context.Xaml, "TextWrapping=\"NoWrap\"", "NoWrap template column should keep TextWrapping=NoWrap.");
        RequireContains(context.Xaml, "TextTrimming=\"WordEllipsis\"", "Custom trimming should be exported.");
        RequireContains(context.Xaml, "MaxLines=\"1\"", "Single-line trimming should keep MaxLines=1.");
    }

    private static void ConfigureDataGridGroupingDoesNotGenerateInvalidBindings(MainWindowViewModel vm)
    {
        var source = ProductsSource();
        source.Fields[0].GroupOrder = 0;
        vm.BindingSources.Add(source);
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        var grid = DataGrid("ProductsGrid", source.Id, 32, 42, 520, 260);
        grid.AllowGrouping = true;
        grid.ShowGroupPanel = true;
        vm.Controls.Add(grid);
    }

    private static void AssertDataGridGroupingDoesNotGenerateInvalidBindings(SmokeContext context)
    {
        RequireContains(context.Xaml, "Группировка:", "Export comments should preserve grouping metadata.");
        RequireContains(context.Xaml, "Title (0)", "Grouping metadata should name the configured group column.");
        RequireNotContains(context.Xaml, "{Binding }", "Grouping export must not generate empty binding markup.");
        RequireNotContains(context.Xaml, "Path=\"\"", "Grouping export must not generate empty binding path.");
        RequireNotContains(context.Xaml, "HeaderTemplate", "Grouping metadata should not generate invalid header templates.");
        if (context.ViewModel.ExportDiagnostics.Any(item =>
                item.Message.Contains("DataGrid grouping exported as metadata only", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Real DataGrid runtime binding should not warn that grouping is metadata-only.");
        }
    }

    private static void ConfigureDataGridManyColumnsShowsHorizontalScroll(MainWindowViewModel vm)
    {
        var source = new BindingSourceModel
        {
            Id = "wide-source",
            Name = "WideSource",
            Path = "WideRows",
            ItemTypeName = "WideRow"
        };
        for (var index = 1; index <= 12; index++)
            source.Fields.Add(Field($"Column {index}", $"Column{index}", $"Value {index}", "string", "160"));

        vm.BindingSources.Add(source);
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        vm.Controls.Add(DataGrid("WideGrid", source.Id, 32, 42, 360, 240));
    }

    private static void AssertDataGridManyColumnsShowsHorizontalScroll(SmokeContext context)
    {
        RequireContains(context.Xaml, "HorizontalScrollBarVisibility=\"Auto\"", "Many-column DataGrid should enable horizontal scroll.");
        RequireContains(context.Xaml, "VerticalScrollBarVisibility=\"Auto\"", "Many-column DataGrid should enable vertical scroll.");
        RequireContains(context.Xaml, "Width=\"160\"", "Column pixel widths should be exported.");
        RequireContains(context.Xaml, "SortMemberPath=\"Column12\"", "All wide columns should be exported with sort paths.");
    }

    private static void AssertDataGridPreviewExportSettingsMatch(SmokeContext context)
    {
        RequireContains(context.Xaml, "CanUserSortColumns=\"True\"", "Preview/export DataGrid settings should include sorting.");
        RequireContains(context.Xaml, "CanUserResizeColumns=\"True\"", "Preview/export DataGrid settings should include resizing.");
        RequireContains(context.Xaml, "HorizontalScrollBarVisibility=\"Auto\"", "Preview/export DataGrid settings should include horizontal scroll.");
        RequireContains(context.Xaml, "VerticalScrollBarVisibility=\"Auto\"", "Preview/export DataGrid settings should include vertical scroll.");
        RequireContains(context.Xaml, "TextWrapping=\"Wrap\"", "Preview/export DataGrid settings should include wrapping.");
        RequireContains(context.Xaml, "TextTrimming=\"None\"", "Preview/export DataGrid settings should include trimming.");
    }

    private static void ConfigureSqlDataGridExport(MainWindowViewModel vm)
    {
        var source = SqlCustomersSource();
        vm.BindingSources.Add(source);
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        vm.Controls.Add(DataGrid("CustomersGrid", source.Id, 32, 42, 720, 360));
    }

    private static void ConfigureSqlDataGridExportConnectionAllowed(MainWindowViewModel vm)
    {
        vm.ExportSqlConnectionString = true;
        ConfigureSqlDataGridExport(vm);
    }

    private static void ConfigureSqlDataGridRussianColumnsExportAllowed(MainWindowViewModel vm)
    {
        vm.ExportSqlConnectionString = true;
        var source = SqlRuntimeMismatchSource("sql-russian-columns-source", "RuntimeRows", "SqlRow");
        vm.BindingSources.Add(source);
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        vm.Controls.Add(DataGrid("RuntimeGrid", source.Id, 32, 42, 720, 360));
    }

    private static void AssertSqlDataGridExportGeneratesItemsSourceProperty(SmokeContext context)
    {
        RequireContains(context.Xaml, "ItemsSource=\"{Binding CustomersView}\"", "SQL DataGrid should bind to generated ViewModel collection.");
        RequireContains(context.Xaml, "SelectedItem=\"{Binding SelectedCustomerRow, Mode=TwoWay}\"", "SQL DataGrid should bind SelectedItem to generated ViewModel property.");
        RequireContains(context.CSharp, "public ObservableCollection<CustomerRow> Customers { get; } = new();", "SQL export should generate source collection.");
        RequireContains(context.CSharp, "public ObservableCollection<CustomerRow> CustomersView { get; } = new();", "SQL export should generate view collection.");
        RequireContains(context.CSharp, "private const string CustomerRowSqlConnectionString = \"TODO: set SQL Server connection string\";", "SQL export should not leak the designer connection string.");
        RequireNotContains(context.CSharp, "Password=secret", "SQL export must not leak a password from the designer connection string.");
        RequireContains(context.CSharp, "LoadCustomerRowFromSql", "SQL export should provide a runtime loader method.");
        RequireNotContains(context.CSharp, "SeedCustomerRow();", "Real SQL export should not seed fake rows.");
    }

    private static void AssertSqlDataGridExportGeneratesRowDto(SmokeContext context)
    {
        RequireContains(context.CSharp, "public partial class CustomerRow : ObservableObject", "SQL export should generate row DTO.");
        RequireContains(context.CSharp, "private int id;", "SQL DTO should include Id.");
        RequireContains(context.CSharp, "private string name;", "SQL DTO should include Name.");
        RequireContains(context.CSharp, "private string email;", "SQL DTO should include Email.");
        RequireContains(context.CSharp, "private string status;", "SQL DTO should include Status.");
        RequireContains(GetRequiredPackagesText(context), "CommunityToolkit.Mvvm", "Generated project should include CommunityToolkit for generated DTO/ViewModel.");
        RequireContains(GetRequiredPackagesText(context), "Microsoft.Data.SqlClient", "Generated project should include SqlClient for optional SQL loader.");
    }

    private static void AssertSqlDataGridWithConnectionAndQueryGeneratesSqlLoader(SmokeContext context)
    {
        RequireContains(context.CSharp, "private const string CustomerRowSqlConnectionString = \"Server=.;Database=Demo;User Id=designer;Password=secret;TrustServerCertificate=True\";", "SQL export should use the configured connection string when explicitly allowed.");
        RequireContains(context.CSharp, "private const string CustomerRowSqlCommandText = @\"SELECT Id, Name, Email, Status FROM dbo.Customers\";", "SQL export should use the configured query.");
        RequireContains(context.CSharp, "LoadCustomerRowFromSql();", "Generated ViewModel constructor should call SQL loader when connection string export is allowed.");
        RequireContains(context.CSharp, "public void LoadCustomerRowFromSql()", "SQL export should generate a runtime SQL loader.");
        RequireContains(context.CSharp, "using var connection = new SqlConnection(CustomerRowSqlConnectionString);", "SQL loader should create a SqlConnection.");
        RequireContains(context.CSharp, "using var reader = command.ExecuteReader();", "SQL loader should execute the configured query.");
        RequireNotContains(context.CSharp, "SeedCustomerRow();", "Real SQL export should not seed demo rows.");
        RequireNotContains(context.CSharp, "Customers.Add(new CustomerRow", "Real SQL export should not generate fake customer rows.");
        RequireTraceEvent(context, "EXPORT_SQL_DATAGRID_RUNTIME_MODE");
        RequireTraceEvent(context, "EXPORT_SQL_LOADER_GENERATED");
    }

    private static void AssertSqlDataGridConnectionStringExportedWhenAllowed(SmokeContext context)
    {
        RequireContains(context.CSharp, "Password=secret", "Connection string should be exported only when the explicit setting is enabled.");
        RequireTraceEvent(context, "EXPORT_SQL_CONNECTION_STRING_USED");
    }

    private static void AssertSqlDataGridConnectionStringNotExportedWhenDisabled(SmokeContext context)
    {
        RequireContains(context.CSharp, "private const string CustomerRowSqlConnectionString = \"TODO: set SQL Server connection string\";", "Disabled SQL secret export should generate a TODO connection string.");
        RequireNotContains(context.CSharp, "Password=secret", "Disabled SQL secret export must not leak the designer connection string.");
        RequireContains(context.CSharp, "public void LoadCustomerRowFromSql()", "Disabled SQL secret export should still include a clear placeholder loader.");
        RequireNotContains(context.CSharp, "LoadCustomerRowFromSql();", "Placeholder SQL loader should not run automatically without an exported connection string.");
    }

    private static void AssertSqlDataGridQueryExported(SmokeContext context)
    {
        RequireContains(context.CSharp, "SELECT Id, Name, Email, Status FROM dbo.Customers", "Generated SQL loader should preserve the user's query.");
        RequireTraceEvent(context, "EXPORT_SQL_QUERY_USED");
    }

    private static void AssertSqlDataGridGeneratesSqlClientPackageReference(SmokeContext context)
    {
        RequireContains(GetRequiredPackagesText(context), "Microsoft.Data.SqlClient", "SQL Server runtime loader should require Microsoft.Data.SqlClient.");
        RequireTraceEvent(context, "EXPORT_SQL_PACKAGE_REFERENCE_GENERATED");
    }

    private static void AssertSqlDataGridColumnMappingUsesOriginalColumnNamesForReader(SmokeContext context)
    {
        RequireContains(context.Xaml, "Binding=\"{Binding Кто_выгрузил}\"", "Runtime AXAML should bind to sanitized DTO property for SQL column with spaces.");
        RequireContains(context.CSharp, "private string кто_выгрузил;", "Generated DTO should contain sanitized SQL column property.");
        RequireContains(context.CSharp, "Кто_выгрузил = ReadString(reader, @\"Кто выгрузил\")", "SQL reader should use the original column name while assigning the sanitized property.");
        RequireContains(context.CSharp, "Рабочая_станция = ReadString(reader, @\"Рабочая станция\")", "SQL reader should preserve original Russian column names.");
        RequireTraceEvent(context, "EXPORT_SQL_COLUMN_MAPPING");
    }

    private static void AssertSqlDataGridWithoutQueryDoesNotGenerateFakeSqlData(SmokeContext context)
    {
        RequireTraceEvent(context, "EXPORT_DATAGRID_EMPTY_COLLECTION_GENERATED");
        RequireTraceEvent(context, "EXPORT_SQL_LOADER_SKIPPED");
        RequireNotContains(context.CSharp, "LoadSqlRowFromSql", "Unconfigured SQL source without query/table should not generate a SQL loader.");
        RequireNotContains(context.CSharp, "SeedSqlRow();", "NoData SQL export should not seed fake rows.");
        RequireNotContains(context.CSharp, "SqlSource.Add(new SqlRow", "NoData SQL export should not add fake rows.");
        RequireNotContains(GetRequiredPackagesText(context), "Microsoft.Data.SqlClient", "Unconfigured SQL source without query/table should not require SqlClient.");
    }

    private static void AssertDemoDataStillGeneratesSeedRowsOnlyInDemoMode(SmokeContext context)
    {
        RequireTraceEvent(context, "EXPORT_DATAGRID_DEMO_ROWS_GENERATED");
        RequireContains(context.CSharp, "SeedSqlRow();", "DemoData SQL export should seed explicit demo rows.");
        RequireContains(context.CSharp, "SqlSource.Add(new SqlRow", "DemoData SQL export should add sample rows.");
        RequireNotContains(context.CSharp, "LoadSqlRowFromSql", "DemoData without query/table should not generate a SQL runtime loader.");
    }

    private static void AssertSqlDataGridPreviewUsesRealSqlRowsWhenSourceConfigured(SmokeContext context)
    {
        using var provider = UseSqlPreviewRowsProvider((_, _, _, _) => SqlPreviewRows(("Id", "9001"), ("Name", "Real SQL customer"), ("Email", "real-sql@example.com"), ("Status", "Loaded")));
        var source = GetOnlySqlSource(context);
        source.AllowPreviewSampleFallback = false;
        source.UseRealPreviewRowsIfAvailable = true;

        var rows = context.ViewModel.LoadPreviewRowsForBindingSourceAsync(source).GetAwaiter().GetResult();

        RequireEqual(1, rows.Count, "Configured SQL preview should load rows from SQL provider.");
        RequireEqual("Real SQL customer", rows[0]["Name"], "Configured SQL preview should display real provider rows.");
        RequireEqual("RealData", source.PreviewRowsDataKind, "Configured SQL preview should be marked as real data.");
        RequireTraceEvent(context, "PREVIEW_DATAGRID_DATA_MODE");
        RequireTraceEvent(context, "DATAGRID_PREVIEW_REAL_ROWS_APPLIED");
        RequireNoTraceEvent(context, "PREVIEW_DATAGRID_UNEXPECTED_DEMO_FALLBACK");
    }

    private static void AssertSqlDataGridPreviewDoesNotUseDemoRowsForConfiguredSql(SmokeContext context)
    {
        using var provider = UseSqlPreviewRowsProvider((_, _, _, _) => SqlPreviewRows(("Id", "42"), ("Name", "Only real row"), ("Email", "real-only@example.com"), ("Status", "Real")));
        var source = GetOnlySqlSource(context);
        source.AllowPreviewSampleFallback = false;
        source.UseRealPreviewRowsIfAvailable = true;

        RequireEqual(true, ShouldSuppressSyntheticPreviewRows(source), "Configured real SQL source should suppress synthetic preview rows while real rows load.");
        var rows = context.ViewModel.LoadPreviewRowsForBindingSourceAsync(source).GetAwaiter().GetResult();
        var joined = string.Join("|", rows.SelectMany(row => row.Values));

        RequireContains(joined, "Only real row", "Configured SQL preview should contain real provider values.");
        RequireNotContains(joined, "Ada Lovelace", "Configured SQL preview must not fall back to schema sample values.");
        RequireNotContains(joined, "Sample", "Configured SQL preview must not show generic sample rows.");
        RequireNotContains(joined, "текст", "Configured SQL preview must not show synthetic text rows.");
    }

    private static void AssertSqlDataGridPreviewUsesDemoOnlyWhenExplicitlyEnabled(SmokeContext context)
    {
        var source = GetOnlySqlSource(context);
        var rows = context.ViewModel.LoadPreviewRowsForBindingSourceAsync(source).GetAwaiter().GetResult();

        if (rows.Count == 0)
            throw new InvalidOperationException("Explicit demo mode should produce preview demo rows.");

        RequireEqual("DemoData", source.PreviewRowsDataKind, "Unconfigured SQL with demo fallback should be marked as DemoData.");
        RequireTraceEvent(context, "PREVIEW_DATAGRID_DATA_MODE");
        RequireTraceEvent(context, "DATAGRID_PREVIEW_SAMPLE_ROWS_USED");
    }

    private static void AssertSqlDataGridPreviewShowsNoDataWhenNoSourceAndDemoDisabled(SmokeContext context)
    {
        var source = GetOnlySqlSource(context);
        var rows = context.ViewModel.LoadPreviewRowsForBindingSourceAsync(source).GetAwaiter().GetResult();

        RequireEqual(0, rows.Count, "Unconfigured SQL with demo disabled should not generate fake preview rows.");
        RequireEqual("SchemaOnly", source.PreviewRowsDataKind, "Unconfigured SQL with demo disabled should be schema-only/no-data.");
        RequireTraceEvent(context, "PREVIEW_DATAGRID_DATA_MODE");
    }

    private static void AssertSqlPreviewCacheInvalidatesWhenQueryChanges(SmokeContext context)
    {
        using var provider = UseSqlPreviewRowsProvider((_, _, _, query) =>
            query?.Contains("CustomersV2", StringComparison.OrdinalIgnoreCase) == true
                ? SqlPreviewRows(("Id", "2"), ("Name", "Query B"), ("Email", "b@example.com"), ("Status", "B"))
                : SqlPreviewRows(("Id", "1"), ("Name", "Query A"), ("Email", "a@example.com"), ("Status", "A")));
        var source = GetOnlySqlSource(context);
        source.AllowPreviewSampleFallback = false;
        source.UseRealPreviewRowsIfAvailable = true;

        source.SourceQuery = "SELECT Id, Name, Email, Status FROM dbo.Customers";
        var rowsA = context.ViewModel.LoadPreviewRowsForBindingSourceAsync(source).GetAwaiter().GetResult();
        source.SourceQuery = "SELECT Id, Name, Email, Status FROM dbo.CustomersV2";
        var rowsB = context.ViewModel.LoadPreviewRowsForBindingSourceAsync(source).GetAwaiter().GetResult();

        RequireEqual("Query A", rowsA[0]["Name"], "Initial SQL preview query should load query A rows.");
        RequireEqual("Query B", rowsB[0]["Name"], "Changed SQL preview query should load query B rows.");
        RequireNotEqual(rowsA[0]["Name"], rowsB[0]["Name"], "SQL preview rows must not stay stale after query changes.");
    }

    private static void AssertSqlPreviewCacheKeyIncludesSourceKeyAndQueryHash(SmokeContext context)
    {
        using var provider = UseSqlPreviewRowsProvider((_, _, _, query) =>
            query?.Contains("Orders", StringComparison.OrdinalIgnoreCase) == true
                ? SqlPreviewRows(("Id", "77"), ("Name", "Orders source"), ("Email", "orders@example.com"), ("Status", "Orders"))
                : SqlPreviewRows(("Id", "11"), ("Name", "Customers source"), ("Email", "customers@example.com"), ("Status", "Customers")));
        var customers = GetOnlySqlSource(context);
        customers.AllowPreviewSampleFallback = false;
        customers.UseRealPreviewRowsIfAvailable = true;

        var orders = SqlCustomersSource();
        orders.Id = "sql-orders-preview-source";
        orders.Name = "SqlOrders";
        orders.Path = "Orders";
        orders.SourceTableName = "Orders";
        orders.SourceQuery = "SELECT Id, Name, Email, Status FROM dbo.Orders";
        orders.AllowPreviewSampleFallback = false;
        orders.UseRealPreviewRowsIfAvailable = true;
        context.ViewModel.BindingSources.Add(orders);

        var customerRows = context.ViewModel.LoadPreviewRowsForBindingSourceAsync(customers).GetAwaiter().GetResult();
        var orderRows = context.ViewModel.LoadPreviewRowsForBindingSourceAsync(orders).GetAwaiter().GetResult();

        RequireEqual("Customers source", customerRows[0]["Name"], "First SQL source should keep its own preview rows.");
        RequireEqual("Orders source", orderRows[0]["Name"], "Second SQL source should keep its own preview rows.");
        RequireNotEqual(customerRows[0]["Name"], orderRows[0]["Name"], "SQL preview cache/source identity must not mix rows between sources.");
    }

    private static void AssertSqlPreviewUsesDistinctProviderRowsWithoutSyntheticExpansion(SmokeContext context)
    {
        using var provider = UseSqlPreviewRowsProvider((_, _, _, _) => SqlPreviewRowsMany(
            new[] { ("Id", "1"), ("Name", "Иван"), ("Email", "ivan@example.com"), ("Status", "Москва") },
            new[] { ("Id", "2"), ("Name", "Пётр"), ("Email", "petr@example.com"), ("Status", "Казань") },
            new[] { ("Id", "3"), ("Name", "Анна"), ("Email", "anna@example.com"), ("Status", "Тула") }));

        var source = GetOnlySqlSource(context);
        source.AllowPreviewSampleFallback = false;
        source.UseRealPreviewRowsIfAvailable = true;

        var rows = context.ViewModel.LoadPreviewRowsForBindingSourceAsync(source).GetAwaiter().GetResult();

        RequireEqual(3, rows.Count, "SQL preview must preserve the exact number of rows returned by the provider.");
        RequireEqual("Иван", rows[0]["Name"], "SQL preview row 1 must keep the provider value.");
        RequireEqual("Пётр", rows[1]["Name"], "SQL preview row 2 must not be copied from row 1.");
        RequireEqual("Анна", rows[2]["Name"], "SQL preview row 3 must not be copied from row 1.");
        RequireNotEqual(rows[0]["Name"], rows[1]["Name"], "SQL preview must not clone the first row down the table.");
        RequireNotEqual(rows[0]["Status"], rows[2]["Status"], "SQL preview must not synthesize repeated rows from sample values.");

        var previewWindowCode = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "PreviewWindow.axaml.cs"), Encoding.UTF8);
        RequireContains(previewWindowCode, "TryGetCachedPreviewRows(control.BindingSourceId", "Preview Window should distinguish missing cache from an empty SQL result.");
        RequireContains(previewWindowCode, "ShouldSuppressSyntheticRowsForPreview(previewBindingSource)", "Preview Window should suppress synthetic fallback for configured SQL sources.");
        RequireContains(previewWindowCode, "PREVIEW_DATAGRID_SYNTHETIC_ROWS_SUPPRESSED", "Preview Window should log when SQL synthetic fallback is blocked.");
        RequireNotContains(previewWindowCode, "GetCachedPreviewRows(control.BindingSourceId).Count > 0", "Preview Window must not use Count > 0 as the cache-exists check.");
        RequireNotContains(string.Join("|", rows.SelectMany(row => row.Values)), "Ada Lovelace", "Configured SQL preview must not fall back to BindingSource sample values.");
    }

    private static BindingSourceModel GetOnlySqlSource(SmokeContext context)
    {
        return context.ViewModel.BindingSources.First(source => string.Equals(source.SourceKind, "SqlServer", StringComparison.OrdinalIgnoreCase));
    }

    private static Task<IReadOnlyList<Dictionary<string, string>>> SqlPreviewRows(params (string Key, string Value)[] values)
    {
        IReadOnlyList<Dictionary<string, string>> rows = new[]
        {
            values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
        };
        return Task.FromResult(rows);
    }

    private static Task<IReadOnlyList<Dictionary<string, string>>> SqlPreviewRowsMany(params (string Key, string Value)[][] rows)
    {
        IReadOnlyList<Dictionary<string, string>> result = rows
            .Select(row => row.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase))
            .ToList();
        return Task.FromResult(result);
    }

    private static IDisposable UseSqlPreviewRowsProvider(
        Func<string, string, string, string?, Task<IReadOnlyList<Dictionary<string, string>>>> provider)
    {
        var loaderType = typeof(MainWindowViewModel).Assembly.GetType("FormDesigner.DesignerSystem.Binding.SqlPreviewDataLoader")
            ?? throw new InvalidOperationException("SqlPreviewDataLoader type was not found.");
        var property = loaderType.GetProperty("TestRowsProviderAsync", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new InvalidOperationException("SqlPreviewDataLoader.TestRowsProviderAsync was not found.");
        var previous = property.GetValue(null);
        property.SetValue(null, provider);
        return new DisposeAction(() => property.SetValue(null, previous));
    }

    private static bool ShouldSuppressSyntheticPreviewRows(BindingSourceModel source)
    {
        var loaderType = typeof(MainWindowViewModel).Assembly.GetType("FormDesigner.DesignerSystem.Binding.PreviewRowsLoader")
            ?? throw new InvalidOperationException("PreviewRowsLoader type was not found.");
        var method = loaderType.GetMethod(
            "ShouldSuppressSyntheticRows",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            types: new[] { typeof(BindingSourceModel) },
            modifiers: null)
            ?? throw new InvalidOperationException("PreviewRowsLoader.ShouldSuppressSyntheticRows was not found.");
        return (bool)(method.Invoke(null, new object?[] { source }) ?? false);
    }

    private static void ConfigureSqlDataGridPreviewRuntimeRowsMatch(MainWindowViewModel vm)
    {
        var source = SqlRuntimeMismatchSource("sql-preview-runtime-source", "RuntimeRows", "SqlRow");
        source.UseRealPreviewRowsIfAvailable = false;
        source.UseDemoData = true;
        vm.BindingSources.Add(source);
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        vm.Controls.Add(DataGrid("RuntimeGrid", source.Id, 32, 42, 720, 360));
    }

    private static void AssertSqlDataGridPreviewRuntimeRowsMatch(SmokeContext context)
    {
        RequireContains(context.Xaml, "ItemsSource=\"{Binding RuntimeRowsView}\"", "SQL DataGrid runtime should bind to generated ViewModel collection.");
        RequireGeneratedMainWindowViewModelAssignment(context, "Generated code-behind should create a runtime ViewModel.");
        RequireContains(context.CSharp, "public ObservableCollection<SqlRow> RuntimeRows { get; } = new();", "Generated ViewModel should expose source collection.");
        RequireContains(context.CSharp, "public ObservableCollection<SqlRow> RuntimeRowsView { get; } = new();", "Generated ViewModel should expose DataGrid view collection.");
        RequireContains(context.CSharp, "SeedSqlRow();", "Generated ViewModel constructor should seed SQL sample rows.");
        RequireContains(context.CSharp, "RuntimeRows.Add(new SqlRow", "Runtime collection should receive sample rows.");
        RequireContains(context.CSharp, "RUNTIME_DATAGRID_COLLECTION_CREATED", "Generated runtime should trace created DataGrid collection row count.");
        RequireContains(context.Xaml, "Binding=\"{Binding Кто_выгрузил}\"", "Runtime AXAML should bind to sanitized DTO property for SQL column with spaces.");
        RequireContains(context.Xaml, "SortMemberPath=\"Кто_выгрузил\"", "Runtime sorting should use sanitized DTO property for SQL column with spaces.");
        RequireNotContains(context.Xaml, "Binding=\"{Binding Кто выгрузил}\"", "Runtime AXAML must not bind to raw SQL column name that is not a C# property.");
        RequireContains(context.CSharp, "private string кто_выгрузил;", "Generated DTO should contain the sanitized SQL column property.");
        RequireTraceEvent(context, "DATAGRID_PREVIEW_ROWS_CREATED");
        RequireTraceEvent(context, "EXPORT_DATAGRID_ITEMSSOURCE_GENERATED");
        RequireTraceEvent(context, "EXPORT_VIEWMODEL_COLLECTION_CREATED");
        RequireTraceEvent(context, "EXPORT_DATAGRID_ROWS_GENERATED");
        RequireTraceEvent(context, "EXPORT_DATAGRID_RUNTIME_BINDING_START");
        RequireTraceEvent(context, "EXPORT_DATAGRID_DATACONTEXT_GENERATED");
        RequireTraceEvent(context, "EXPORT_DATAGRID_LOADER_GENERATED");
        RequireTraceEvent(context, "EXPORT_COLUMN_BINDING_VALIDATED");
        RequireGeneratedViewModelCollectionHasRows(context, "RuntimeRowsView", 1);
    }

    private static void ConfigureUnconfiguredSqlDataGridDemoRows(MainWindowViewModel vm)
    {
        var source = UnconfiguredSqlSource(allowDemoRows: true);
        vm.BindingSources.Add(source);
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        var grid = DataGrid("DataGrid1", source.Id, 32, 42, 720, 360);
        grid.ShowFilterRow = true;
        grid.ShowGroupPanel = true;
        vm.Controls.Add(grid);
        vm.LoadPreviewRowsForBindingSourceAsync(source).GetAwaiter().GetResult();
    }

    private static void ConfigureUnconfiguredSqlDataGridNoData(MainWindowViewModel vm)
    {
        var source = UnconfiguredSqlSource(allowDemoRows: false);
        source.PreviewRowMode = BindingSourceModel.PreviewRowModeSchemaOnly;
        vm.BindingSources.Add(source);
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        var grid = DataGrid("DataGrid1", source.Id, 32, 42, 720, 360);
        grid.ShowFilterRow = true;
        grid.ShowGroupPanel = true;
        vm.Controls.Add(grid);
        vm.LoadPreviewRowsForBindingSourceAsync(source).GetAwaiter().GetResult();
    }

    private static void AssertSqlDataGridWithoutConnectionDoesNotPretendRealData(SmokeContext context)
    {
        RequireTraceEvent(context, "DATAGRID_PREVIEW_DATA_MODE");
        RequireTraceEvent(context, "DATAGRID_EXPORT_DATA_MODE");
        RequireTraceEvent(context, "EXPORT_DATAGRID_SOURCE_NOT_CONFIGURED_WARNING");
        RequireTraceEvent(context, "EXPORT_SQL_LOADER_SKIPPED");
        RequireNotContains(context.CSharp, "private const string SqlRowSqlConnectionString", "Unconfigured SQL export without query/table should not generate a fake SQL connection string.");
        RequireNotContains(context.CSharp, "LoadSqlRowFromSql", "Unconfigured SQL export without query/table should not generate a SQL loader.");
        RequireNotContains(context.CSharp, "Server=.;Database=", "Unconfigured SQL export must not pretend that a real connection exists.");
    }

    private static void AssertDemoDataPreviewExportsDemoRows(SmokeContext context)
    {
        RequireTraceEvent(context, "EXPORT_DATAGRID_DEMO_ROWS_GENERATED");
        RequireTraceEvent(context, "EXPORT_DATAGRID_RUNTIME_BINDING_VALIDATED");
        RequireContains(context.Xaml, "ItemsSource=\"{Binding SqlSourceView}\"", "Demo DataGrid runtime should bind to the generated demo collection.");
        RequireGeneratedMainWindowViewModelAssignment(context, "Demo DataGrid runtime should assign DataContext.");
        RequireContains(context.CSharp, "SeedSqlRow();", "Demo DataGrid runtime should seed rows.");
        RequireContains(context.CSharp, "SqlSource.Add(new SqlRow", "Demo DataGrid runtime should add sample rows.");
        RequireContains(context.CSharp, "Field1 = 1", "Demo rows should use the same numeric demo generator as Preview.");
        RequireGeneratedViewModelCollectionHasRows(context, "SqlSourceView", 1);
    }

    private static void AssertNoDataPreviewExportsEmptyCollectionWithWarning(SmokeContext context)
    {
        RequireTraceEvent(context, "EXPORT_DATAGRID_EMPTY_COLLECTION_GENERATED");
        RequireTraceEvent(context, "EXPORT_DATAGRID_SOURCE_NOT_CONFIGURED_WARNING");
        RequireContains(context.Xaml, "ItemsSource=\"{Binding SqlSourceView}\"", "NoData export should still expose an explicit empty collection binding.");
        RequireContains(context.CSharp, "public ObservableCollection<SqlRow> SqlSource { get; } = new();", "NoData export should create the source collection.");
        RequireNotContains(context.CSharp, "SqlSource.Add(new SqlRow", "NoData export should not silently seed demo rows.");
        RequireGeneratedViewModelCollectionRowCount(context, "SqlSourceView", expectedRows: 0);
    }

    private static void AssertPreviewRuntimeDataModeMatches(SmokeContext context)
    {
        RequireTraceEvent(context, "DATAGRID_PREVIEW_DATA_MODE");
        RequireTraceEvent(context, "DATAGRID_EXPORT_DATA_MODE");
        RequireTraceEvent(context, "EXPORT_DATAGRID_DEMO_ROWS_GENERATED");
        RequireGeneratedViewModelCollectionHasRows(context, "SqlSourceView", 1);
    }

    private static void AssertExportedDataGridShowsRowsWhenDemoDataEnabled(SmokeContext context)
    {
        RequireContains(context.Xaml, "<DataGrid", "Demo DataGrid should export a real runtime DataGrid.");
        RequireContains(context.Xaml, "ItemsSource=\"{Binding SqlSourceView}\"", "Demo DataGrid should have runtime ItemsSource.");
        RequireContains(context.CSharp, "RUNTIME_DATAGRID_COLLECTION_CREATED", "Generated runtime should trace DataGrid collection rows.");
        RequireGeneratedViewModelCollectionHasRows(context, "SqlSourceView", 6);
    }

    private static void AssertExportedDataGridShowsEmptyWithClearWarningWhenNoSourceAndNoDemo(SmokeContext context)
    {
        RequireTraceEvent(context, "EXPORT_DATAGRID_EMPTY_COLLECTION_GENERATED");
        RequireTraceEvent(context, "EXPORT_DATAGRID_SOURCE_NOT_CONFIGURED_WARNING");
        RequireNotContains(context.CSharp, "SeedSqlRow();", "NoData export should not keep a fake seed hook.");
        RequireGeneratedViewModelCollectionRowCount(context, "SqlSourceView", expectedRows: 0);
    }

    private static void AssertGeneratedDataGridAppAxamlIncludesDataGridTheme(SmokeContext context)
    {
        using var export = ExportToTemporaryProject(context);
        var appXamlPath = Path.Combine(export.Path, "App.axaml");
        RequireFileExists(appXamlPath, "Exported project should include App.axaml.");
        var appXaml = File.ReadAllText(appXamlPath, Encoding.UTF8);
        RequireContains(appXaml, "avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml", "Real DataGrid exported app should include Avalonia.Controls.DataGrid Fluent theme.");
    }

    private static void AssertGeneratedDataGridRuntimeSelfCheckGenerated(SmokeContext context)
    {
        RequireTraceEvent(context, "EXPORT_DATAGRID_RUNTIME_SELF_CHECK_GENERATED");
        RequireContains(context.CSharp, "RUNTIME_DATAGRID_ATTACHED", "Generated real DataGrid runtime should log attached ItemsSource diagnostics.");
        RequireContains(context.CSharp, "RUNTIME_DATAGRID_BINDING_FAILED", "Generated real DataGrid runtime should log binding failures.");
        RequireContains(context.CSharp, "this.FindControl<DataGrid>(\"DataGrid1\")", "Generated runtime self-check should inspect the exported DataGrid control.");
    }

    private static void AssertGeneratedDataGridWithFilterAndGroupPanelStillShowsRows(SmokeContext context)
    {
        RequireContains(context.Xaml, "RowDefinitions=\"Auto,Auto,*\"", "DataGrid host should keep a row area after group and filter rows.");
        RequireContains(context.Xaml, "Grid.Row=\"2\"", "DataGrid should be placed in the star-sized row below group/filter rows.");
        RequireContains(context.Xaml, "ItemsSource=\"{Binding SqlSourceView}\"", "DataGrid with group/filter host should still bind runtime rows.");
        RequireContains(context.CSharp, "Field1Filter", "Filter bindings should exist but stay empty by default.");
        RequireGeneratedViewModelCollectionHasRows(context, "SqlSourceView", 1);
    }

    private static void AssertDataGridWithoutSourceExportsEmptyCollection(SmokeContext context)
    {
        RequireContains(context.Xaml, "ItemsSource=\"{Binding SqlSourceView}\"", "Empty DataGrid should keep an explicit runtime ItemsSource binding.");
        RequireContains(context.CSharp, "public ObservableCollection<SqlRow> SqlSource { get; } = new();", "Empty DataGrid should still generate its source collection.");
        RequireContains(context.CSharp, "ApplySqlRowFilter();", "Empty DataGrid should safely initialize its filtered view.");
        RequireNotContains(context.CSharp, "SeedSqlRow", "DataGrid without a configured source and demo disabled must not generate a seed method or call.");
        RequireNotContains(context.CSharp, "SqlSource.Add(new SqlRow", "Empty mode must not add synthetic rows.");
        RequireTraceEvent(context, "DATAGRID_RUNTIME_DATA_MODE_RESOLVED");
        RequireTraceEvent(context, "DATAGRID_DEMO_SEED_GENERATION_SKIPPED");
        RequireGeneratedViewModelCollectionRowCount(context, "SqlSourceView", expectedRows: 0);
    }

    private static void AssertDataGridDemoRowsGeneratedOnlyWhenExplicitlyEnabled(SmokeContext context)
    {
        var source = context.ViewModel.BindingSources.Single();
        RequireEqual(true, source.UseDemoData, "Demo smoke configuration must opt in explicitly through UseDemoData.");
        RequireContains(context.CSharp, "SeedSqlRow();", "Explicit Demo mode should call the generated seed method.");
        RequireContains(context.CSharp, "private void SeedSqlRow()", "Explicit Demo mode should generate a seed method.");
        RequireContains(context.CSharp, "SqlSource.Add(new SqlRow", "Explicit Demo mode should populate the runtime collection.");
        RequireTraceEvent(context, "DATAGRID_DEMO_SEED_GENERATED");
    }

    private static void AssertAxamlPreviewEmptyDataGridKeepsGroupPanel(SmokeContext context)
    {
        var payload = context.ViewModel.GenerateExportedAxamlPreviewSnapshot();
        RequireContains(payload.Axaml, "RowDefinitions=\"Auto,Auto,*\"", "RuntimePreview AXAML should preserve group/filter/DataGrid host rows.");
        RequireContains(payload.Axaml, DataGridGroupPanelVisualCatalog.PlaceholderText, "RuntimePreview AXAML should preserve the grouping panel.");
        RequireContains(payload.Axaml, "Grid.Row=\"0\"", "Grouping panel should remain in the first host row.");
        RequireContains(payload.Axaml, "Grid.Row=\"1\"", "Filter row should remain in the second host row.");
        RequireContains(payload.Axaml, "Grid.Row=\"2\"", "DataGrid should remain in the star-sized host row.");
        RequireNotContains(payload.GeneratedCSharp, "SeedSqlRow", "Empty AXAML Preview must use the same no-seed runtime mode as Export.");
    }

    private static void AssertAxamlPreviewAndExportUseSameDataMode(SmokeContext context)
    {
        var payload = context.ViewModel.GenerateExportedAxamlPreviewSnapshot();
        RequireEqual(false, payload.IncludeDemoData, "AXAML Preview payload should carry the export demo-data setting.");
        RequireEqual(context.Xaml, payload.ExportAxaml, "AXAML Preview must start from the exact Export AXAML.");
        RequireNotContains(context.CSharp, "SeedSqlRow", "Export Empty mode must not contain a seed method.");
        RequireNotContains(payload.GeneratedCSharp, "SeedSqlRow", "AXAML Preview Empty mode must not contain a seed method.");
    }

    private static void AssertRuntimePreviewDoesNotDropGroupPanelHostRows(SmokeContext context)
    {
        var payload = context.ViewModel.GenerateExportedAxamlPreviewSnapshot();
        RequireContains(payload.ExportAxaml, "RowDefinitions=\"Auto,Auto,*\"", "Export AXAML should generate three DataGrid host rows.");
        RequireContains(payload.Axaml, "RowDefinitions=\"Auto,Auto,*\"", "RuntimePreview transformation must preserve DataGrid host RowDefinitions.");
        RequireContains(payload.Axaml, DataGridGroupPanelVisualCatalog.PlaceholderText, "RuntimePreview transformation must preserve group panel content.");
        RequireContains(payload.Axaml, "MinHeight=\"40\"", "RuntimePreview grouping panel should use the shared visual catalog dimensions.");

        EnsureAvaloniaRuntimeInitialized();
        var loaded = RuntimeAxamlPreviewLoader.Load(payload.Axaml) as UserControl
            ?? throw new InvalidOperationException("RuntimePreview DataGrid payload did not load as UserControl.");
        var grid = loaded.GetLogicalDescendants().OfType<DataGrid>().SingleOrDefault()
            ?? throw new InvalidOperationException("RuntimePreview visual tree does not contain the exported DataGrid.");
        var host = grid.GetLogicalParent() as Grid
            ?? throw new InvalidOperationException("RuntimePreview DataGrid host Grid was dropped during transformation.");
        RequireEqual(3, host.RowDefinitions.Count, "RuntimePreview DataGrid host should retain group, filter and content rows.");
        RequireEqual(2, Grid.GetRow(grid), "RuntimePreview DataGrid should remain in the third host row.");
        if (!host.Children.Any(child => Grid.GetRow(child) == 0)
            || !host.Children.Any(child => Grid.GetRow(child) == 1))
        {
            throw new InvalidOperationException("RuntimePreview DataGrid host lost its grouping panel or filter row.");
        }
    }

    private static void AssertSqlDataGridDoesNotGenerateDemoSeed(SmokeContext context)
    {
        RequireNotContains(context.CSharp, "SeedCustomerRow", "Configured SQL mode must never generate demo seed code.");
        RequireNotContains(context.CSharp, "Customers.Add(new CustomerRow", "Configured SQL mode must not populate fake rows.");
        RequireContains(context.CSharp, "LoadCustomerRowFromSql", "Configured SQL mode should generate its real SQL loader.");
        RequireTraceEvent(context, "DATAGRID_RUNTIME_DATA_MODE_RESOLVED");
        RequireTraceEvent(context, "DATAGRID_DEMO_SEED_GENERATION_SKIPPED");
    }

    private static void ConfigureSqlDataGridAutoPromotesVisualExportToRuntimeRows(MainWindowViewModel vm)
    {
        var source = SqlRuntimeMismatchSource("sql-auto-runtime-source", "AutoRows", "SqlRow");
        source.UseRealPreviewRowsIfAvailable = false;
        source.UseDemoData = true;
        vm.BindingSources.Add(source);
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeVisual;
        vm.Controls.Add(DataGrid("AutoRuntimeGrid", source.Id, 32, 42, 720, 360));
    }

    private static void AssertSqlDataGridAutoPromotesVisualExportToRuntimeRows(SmokeContext context)
    {
        RequireContains(context.Xaml, "<DataGrid", "SQL source-backed DataGrid should be promoted from visual export to real runtime DataGrid.");
        RequireContains(context.Xaml, "ItemsSource=\"{Binding AutoRowsView}\"", "Auto-promoted SQL DataGrid should have a runtime ItemsSource.");
        RequireGeneratedMainWindowViewModelAssignment(context, "Auto-promoted SQL DataGrid should generate and assign DataContext.");
        RequireContains(context.CSharp, "public ObservableCollection<SqlRow> AutoRows { get; } = new();", "Auto-promoted SQL DataGrid should generate source collection.");
        RequireContains(context.CSharp, "public ObservableCollection<SqlRow> AutoRowsView { get; } = new();", "Auto-promoted SQL DataGrid should generate view collection.");
        RequireContains(context.CSharp, "SeedSqlRow();", "Auto-promoted SQL DataGrid should seed runtime rows.");
        RequireTraceEvent(context, "EXPORT_DATAGRID_RUNTIME_BINDING_START");
        RequireTraceEvent(context, "EXPORT_DATAGRID_DATACONTEXT_GENERATED");
        RequireTraceEvent(context, "EXPORT_DATAGRID_LOADER_GENERATED");
        RequireGeneratedViewModelCollectionHasRows(context, "AutoRowsView", 1);
    }

    private static void ConfigureMultiFormSqlDataGridExportBuildsWithDistinctDtos(MainWindowViewModel vm)
    {
        vm.ExportTarget = MainWindowViewModel.ExportTargetMainWindow;
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");

        var source1 = SqlRuntimeMismatchSource("sql-form1-source", "SharedRows", "SqlRow");
        vm.BindingSources.Add(source1);
        vm.Controls.Add(DataGrid("Form1Grid", source1.Id, 32, 42, 720, 320));

        var form2 = vm.CreateNewForm();
        form2.Name = "Form2";
        form2.Document.FormTitle = "Form2";
        vm.FormTitle = "Form2";

        var source2 = SqlRuntimeMismatchSource("sql-form2-source", "SharedRows", "SqlRow");
        vm.BindingSources.Add(source2);
        vm.Controls.Add(DataGrid("Form2Grid", source2.Id, 32, 42, 720, 320));

        SwitchToForm(vm, form1.Id);
    }

    private static void AssertMultiFormSqlDataGridExportBuildsWithDistinctDtos(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml.cs");
        RequireGeneratedFile(context, "Form2.axaml.cs");
        var form2Code = context.GeneratedFiles.First(file => string.Equals(file.Path, "Form2.axaml.cs", StringComparison.OrdinalIgnoreCase)).Content;

        RequireContains(context.CSharp, "public partial class MainWindowSqlRow : ObservableObject", "Active form SQL row DTO should be uniquely scoped by generated form class.");
        RequireContains(form2Code, "public partial class Form2SqlRow : ObservableObject", "Secondary form SQL row DTO should be uniquely scoped by generated form class.");
        RequireNotContains(context.CSharp, "public partial class SqlRow : ObservableObject", "Active form must not emit conflicting shared SqlRow DTO.");
        RequireNotContains(form2Code, "public partial class SqlRow : ObservableObject", "Secondary form must not emit conflicting shared SqlRow DTO.");
        RequireContains(context.Xaml, "ItemsSource=\"{Binding SharedRowsView}\"", "Active form DataGrid should still have runtime ItemsSource.");
        RequireContains(form2Code, "public ObservableCollection<Form2SqlRow> SharedRows { get; } = new();", "Secondary form ViewModel should expose its own runtime collection.");
        RequireContains(form2Code, "RUNTIME_DATAGRID_COLLECTION_CREATED", "Secondary generated runtime should trace created DataGrid rows.");
        RequireTraceEvent(context, "BUILD_SECONDARY_FORM_GENERATION_PURE");
    }

    private static void ConfigureFiveFormSqlDataGridsExportBuilds(MainWindowViewModel vm)
    {
        vm.ExportTarget = MainWindowViewModel.ExportTargetMainWindow;
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");

        for (var index = 1; index <= 5; index++)
        {
            if (index > 1)
            {
                var form = vm.CreateNewForm();
                form.Name = $"Form{index}";
                form.Document.FormTitle = $"Form{index}";
                vm.FormTitle = $"Form{index}";
            }

            var source = SqlRuntimeMismatchSource($"sql-form{index}-source", "SharedRows", "SqlRow");
            vm.BindingSources.Add(source);
            vm.Controls.Add(DataGrid($"Form{index}Grid", source.Id, 32, 42, 720, 320));
        }

        SwitchToForm(vm, form1.Id);
    }

    private static void AssertFiveFormSqlDataGridsExportBuilds(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml.cs");
        for (var index = 2; index <= 5; index++)
            RequireGeneratedFile(context, $"Form{index}.axaml.cs");

        var allCode = GetGeneratedFilesText(context);
        RequireContains(context.CSharp, "public partial class MainWindowSqlRow : ObservableObject", "Active form should use unique SQL DTO.");
        for (var index = 2; index <= 5; index++)
            RequireContains(allCode, $"public partial class Form{index}SqlRow : ObservableObject", $"Form{index} should use unique SQL DTO.");
        RequireNotContains(allCode, "public partial class SqlRow : ObservableObject", "Multi-form SQL export must not generate a shared conflicting SqlRow DTO.");
        RequireContains(allCode, "RUNTIME_DATAGRID_COLLECTION_CREATED", "Generated runtime should trace row collections for multi-form SQL grids.");
    }

    private static void ConfigureMultiFormSqlExportWithGlobalAndDistinctSources(MainWindowViewModel vm)
    {
        vm.ExportTarget = MainWindowViewModel.ExportTargetMainWindow;
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        vm.ExportSqlConnectionString = true;
        vm.UseGlobalSqlServerSettings = true;
        vm.SqlServerName = "global-sql-host";
        vm.SqlDatabaseName = "GlobalDesignerDb";
        vm.SqlAuthenticationMode = SqlServerSettingsModel.AuthWindows;
        vm.SqlTrustServerCertificate = true;
        vm.SqlEncryptConnection = false;
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");

        var source1 = MultiFormSqlSource(
            "orders-global-source",
            "OrdersRows",
            "OrderRow",
            "Orders",
            "SELECT [Id], [Name] FROM [dbo].[Orders]");
        vm.BindingSources.Add(source1);
        vm.Controls.Add(DataGrid("OrdersGrid", source1.Id, 32, 42, 720, 320));

        var form2 = vm.CreateNewForm();
        form2.Name = "Form2";
        form2.Document.FormTitle = "Form2";
        vm.FormTitle = "Form2";
        var source2 = MultiFormSqlSource(
            "audit-explicit-source",
            "AuditRows",
            "AuditRow",
            "AuditEntries",
            "SELECT [Id], [Name] FROM [audit].[AuditEntries]");
        source2.SourceSchemaName = "audit";
        source2.SourceConnectionString = "Server=form2-sql-host;Database=Form2AuditDb;Trusted_Connection=True;TrustServerCertificate=True";
        vm.BindingSources.Add(source2);
        vm.Controls.Add(DataGrid("AuditGrid", source2.Id, 32, 42, 720, 320));

        var form3 = vm.CreateNewForm();
        form3.Name = "Form3";
        form3.Document.FormTitle = "Form3";
        vm.FormTitle = "Form3";
        var source3 = MultiFormSqlSource(
            "reports-global-source",
            "ReportRows",
            "ReportRow",
            "Reports",
            "SELECT [Id], [Name] FROM [reporting].[Reports]");
        source3.SourceSchemaName = "reporting";
        vm.BindingSources.Add(source3);
        vm.Controls.Add(DataGrid("ReportsGrid", source3.Id, 32, 42, 720, 320));

        SwitchToForm(vm, form1.Id);
    }

    private static void AssertMultiFormSqlExportGeneratesConnectionForEveryForm(SmokeContext context)
    {
        var mainCode = RequireGeneratedFile(context, "MainWindow.axaml.cs").Content;
        var form2Code = RequireGeneratedFile(context, "Form2.axaml.cs").Content;
        var form3Code = RequireGeneratedFile(context, "Form3.axaml.cs").Content;

        RequireContains(mainCode, "Data Source=global-sql-host", "Form1 should resolve the global SQL Server name.");
        RequireContains(mainCode, "Initial Catalog=GlobalDesignerDb", "Form1 should resolve the global SQL database.");
        RequireContains(form2Code, "Server=form2-sql-host;Database=Form2AuditDb", "Form2 should preserve its source-specific connection string.");
        RequireContains(form3Code, "Data Source=global-sql-host", "Form3 should receive global SQL settings in its isolated export context.");
        RequireContains(form3Code, "Initial Catalog=GlobalDesignerDb", "Form3 should receive the global SQL database.");
        RequireNotContains(GetGeneratedFilesText(context), "TODO: set SQL Server connection string", "Configured multi-form SQL export must not emit placeholder connection strings.");
        RequireTraceEvent(context, "SECONDARY_FORM_SQL_EXPORT_CONTEXT");
        RequireTraceEvent(context, "EXPORT_FORM_SQL_GRID_RESOLVED");
    }

    private static void AssertMultiFormSqlExportGeneratesLoaderForEveryGrid(SmokeContext context)
    {
        var mainCode = RequireGeneratedFile(context, "MainWindow.axaml.cs").Content;
        var form2Code = RequireGeneratedFile(context, "Form2.axaml.cs").Content;
        var form3Code = RequireGeneratedFile(context, "Form3.axaml.cs").Content;

        RequireContains(mainCode, "public void LoadOrderRowFromSql()", "Form1 SQL loader should be generated.");
        RequireContains(form2Code, "public void LoadAuditRowFromSql()", "Form2 SQL loader should be generated.");
        RequireContains(form3Code, "public void LoadReportRowFromSql()", "Form3 SQL loader should be generated.");
        RequireContains(GetGeneratedFilesText(context), "Microsoft.Data.SqlClient", "The exported project should include the SQL client package for all forms.");
        RequireTraceEvent(context, "EXPORT_FORM_SQL_LOADER_GENERATED");
    }

    private static void AssertMultiFormSqlExportAssignsDataContextForEveryWindow(SmokeContext context)
    {
        var mainCode = RequireGeneratedFile(context, "MainWindow.axaml.cs").Content;
        var form2Code = RequireGeneratedFile(context, "Form2.axaml.cs").Content;
        var form3Code = RequireGeneratedFile(context, "Form3.axaml.cs").Content;
        RequireContains(mainCode, "var viewModel = new MainWindowViewModel();", "Form1 should construct MainWindowViewModel.");
        RequireContains(mainCode, "DataContext = viewModel;", "Form1 should assign its ViewModel as DataContext.");
        RequireContains(form2Code, "var viewModel = new Form2ViewModel();", "Form2 should construct Form2ViewModel.");
        RequireContains(form2Code, "DataContext = viewModel;", "Form2 should assign its ViewModel as DataContext.");
        RequireContains(form3Code, "var viewModel = new Form3ViewModel();", "Form3 should construct Form3ViewModel.");
        RequireContains(form3Code, "DataContext = viewModel;", "Form3 should assign its ViewModel as DataContext.");
        RequireTraceEvent(context, "EXPORT_SECONDARY_FORM_DATACONTEXT_GENERATED");
    }

    private static void AssertMultiFormSqlExportLoadsRowsForSecondaryForms(SmokeContext context)
    {
        var form2Code = RequireGeneratedFile(context, "Form2.axaml.cs").Content;
        var form3Code = RequireGeneratedFile(context, "Form3.axaml.cs").Content;
        RequireContains(form2Code, "LoadAuditRowFromSql();", "Form2 ViewModel constructor should call its SQL loader.");
        RequireContains(form3Code, "LoadReportRowFromSql();", "Form3 ViewModel constructor should call its SQL loader.");
        RequireContains(form2Code, "public ObservableCollection<AuditRow> AuditRowsView { get; } = new();", "Form2 should expose its own ItemsSource collection.");
        RequireContains(form3Code, "public ObservableCollection<ReportRow> ReportRowsView { get; } = new();", "Form3 should expose its own ItemsSource collection.");
    }

    private static void AssertMultiFormSqlExportDoesNotDependOnActiveForm(SmokeContext context)
    {
        var before = context.GeneratedFiles
            .ToDictionary(file => file.Path, file => file.Content, StringComparer.OrdinalIgnoreCase);
        var form3 = context.ViewModel.CurrentProject.Forms.Single(form => string.Equals(form.Name, "Form3", StringComparison.Ordinal));
        SwitchToForm(context.ViewModel, form3.Id);
        context.ViewModel.GenerateXaml();
        var after = context.ViewModel.GeneratedFiles
            .ToDictionary(file => file.Path, file => file.Content, StringComparer.OrdinalIgnoreCase);

        if (before.Count != after.Count)
            throw new InvalidOperationException("Changing active form changed the number of generated form files.");
        foreach (var pair in before)
        {
            if (!after.TryGetValue(pair.Key, out var content) || !string.Equals(pair.Value, content, StringComparison.Ordinal))
                throw new InvalidOperationException($"Changing active form changed generated file '{pair.Key}'.");
        }
    }

    private static void AssertMultiFormSqlExportSupportsDifferentSourcesPerForm(SmokeContext context)
    {
        var mainCode = RequireGeneratedFile(context, "MainWindow.axaml.cs").Content;
        var form2Code = RequireGeneratedFile(context, "Form2.axaml.cs").Content;
        var form3Code = RequireGeneratedFile(context, "Form3.axaml.cs").Content;
        RequireContains(mainCode, "FROM [dbo].[Orders]", "Form1 should use the Orders query.");
        RequireContains(form2Code, "FROM [audit].[AuditEntries]", "Form2 should use the Audit query.");
        RequireContains(form3Code, "FROM [reporting].[Reports]", "Form3 should use the Reports query.");
        RequireNotContains(mainCode, "Form2AuditDb", "Form1 must not receive Form2 SQL settings.");
        RequireNotContains(form3Code, "Form2AuditDb", "Form3 must not receive Form2 SQL settings.");
    }

    private static void ConfigureDllTableDataGridExport(MainWindowViewModel vm)
    {
        var source = DllOrdersSource();
        vm.BindingSources.Add(source);
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        vm.Controls.Add(DataGrid("OrdersGrid", source.Id, 32, 42, 720, 360));
    }

    private static void AssertDllTableDataGridExportGeneratesUnderstandableBinding(SmokeContext context)
    {
        RequireContains(context.Xaml, "ItemsSource=\"{Binding SalesOrdersView}\"", "DLL table DataGrid should bind to a generated ViewModel collection.");
        RequireContains(context.CSharp, "public ObservableCollection<OrderRow> SalesOrders { get; } = new();", "DLL table export should generate source collection.");
        RequireContains(context.CSharp, "public ObservableCollection<OrderRow> SalesOrdersView { get; } = new();", "DLL table export should generate view collection.");
        RequireContains(context.CSharp, "public partial class OrderRow : ObservableObject", "DLL table export should generate portable DTO when runtime DLL reference is not exported.");
        RequireContains(context.Xaml, "Binding=\"{Binding OrderId}\"", "DLL table export should bind OrderId column.");
        RequireContains(context.Xaml, "Binding=\"{Binding CustomerName}\"", "DLL table export should bind CustomerName column.");
        RequireNotContains(context.CSharp, "System.Reflection.MetadataLoadContext", "Generated runtime project must not include designer-only reflection packages.");
    }

    private static void AssertDllDataGridPreviewRuntimeRowsMatch(SmokeContext context)
    {
        RequireContains(context.Xaml, "ItemsSource=\"{Binding SalesOrdersView}\"", "DLL DataGrid runtime should bind to generated ViewModel collection.");
        RequireNotContains(context.CSharp, "SeedOrderRow", "DLL DataGrid must not silently generate demo seed rows.");
        RequireNotContains(context.CSharp, "SalesOrders.Add(new OrderRow", "DLL DataGrid without an exported runtime provider should stay empty.");
        RequireContains(context.CSharp, "RUNTIME_DATAGRID_COLLECTION_CREATED", "Generated runtime should trace DLL DataGrid collection row count.");
        RequireTraceEvent(context, "DATAGRID_DEMO_SEED_GENERATION_SKIPPED");
    }

    private static void AssertManualDataGridPreviewRuntimeRowsMatch(SmokeContext context)
    {
        RequireContains(context.Xaml, "ItemsSource=\"{Binding ManualGridItemsView}\"", "Manual DataGrid runtime should bind to generated ViewModel collection.");
        RequireNotContains(context.CSharp, "SeedManualGridRow", "Manual DataGrid must not generate seed rows unless Demo mode is explicitly enabled.");
        RequireNotContains(context.CSharp, "ManualGridItems.Add(new ManualGridRow", "Manual DataGrid should stay empty when Demo mode is disabled.");
        RequireContains(context.CSharp, "RUNTIME_DATAGRID_COLLECTION_CREATED", "Generated runtime should trace manual DataGrid collection row count.");
        RequireTraceEvent(context, "EXPORT_DATAGRID_ITEMSSOURCE_GENERATED");
    }


    private static void AssertDataGridExportDoesNotGenerateEmptyBindings(SmokeContext context)
    {
        RequireNotContains(context.Xaml, "{Binding }", "DataGrid export must not generate empty binding markup.");
        RequireNotContains(context.Xaml, "Path=\"\"", "DataGrid export must not generate empty binding path.");
        RequireNotContains(context.Xaml, "x:DataType=\"\"", "DataGrid export must not generate empty x:DataType.");
        RequireNotContains(context.Xaml, "HeaderTemplate", "Simple DataGrid export should not need HeaderTemplate.");
    }

    private static void AssertDataGridExportDoesNotRequireManualAxamlFix(SmokeContext context)
    {
        RequireContains(context.Xaml, "<DataGridTextColumn Header=\"OrderId\" Binding=\"{Binding OrderId}\"", "Generated AXAML should contain valid unprefixed DataGridTextColumn.");
        RequireNotContains(context.Xaml, "<dataGrid:DataGridTextColumn", "Generated AXAML must not use invalid prefixed DataGridTextColumn.");
        RequireNotContains(context.Xaml, "DataGridTextColumn.Binding", "Generated AXAML should use attribute bindings.");
        RequireNotContains(context.Xaml, "{Binding }", "Generated AXAML must not need manual binding cleanup.");
    }

    private static void AssertDataGridExportDoesNotGenerateInvalidXDataType(SmokeContext context)
    {
        RequireNotContains(context.Xaml, "x:DataType=\"\"", "DataGrid export must not generate empty x:DataType.");
        RequireNotContains(context.Xaml, "Unable to resolve symbol", "Generated files should not contain unresolved design-time type markers.");
        RequireNotContains(context.Xaml, "CompiledBinding", "Dynamic DataGrid column bindings should stay classic unless a real row type is declared in XAML.");
    }

    private static void ConfigureManualColumnsViaColumnEditor(MainWindowViewModel vm)
    {
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        var grid = DataGrid("ManualGrid", "", 32, 42, 520, 260);
        vm.Controls.Add(grid);
        vm.SelectControls(new[] { grid }, grid);

        var editor = new DataGridColumnEditorViewModel(vm, grid, bindingSource: null);
        editor.ApplyChanges();
        editor.Dispose();
    }

    private static void AssertDataGridExportSupportsManualColumns(SmokeContext context)
    {
        RequireContains(context.Xaml, "ItemsSource=\"{Binding ManualGridItemsView}\"", "Manual DataGrid columns should export an ItemsSource binding.");
        RequireContains(context.Xaml, "<DataGridTextColumn Header=\"Name\" Binding=\"{Binding Name}\"", "Manual text column should export valid binding.");
        RequireContains(context.Xaml, "<DataGridCheckBoxColumn Header=\"IsActive\" Binding=\"{Binding IsActive}\"", "Manual bool column should export CheckBox column.");
        RequireContains(context.CSharp, "public ObservableCollection<ManualGridRow> ManualGridItems { get; } = new();", "Manual columns should generate a ViewModel collection.");
        RequireContains(context.CSharp, "public partial class ManualGridRow : ObservableObject", "Manual columns should generate a DTO row.");
    }

    private static void AssertDataGridExportSupportsSqlSchemaAsDto(SmokeContext context)
    {
        RequireContains(context.CSharp, "public partial class CustomerRow : ObservableObject", "SQL source should export DTO by schema.");
        RequireContains(context.CSharp, "private const string CustomerRowSqlConnectionString = \"TODO: set SQL Server connection string\";", "SQL export should use placeholder connection string.");
        RequireContains(context.CSharp, "LoadCustomerRowFromSql", "SQL export should include a clear placeholder loader.");
    }

    private static void AssertDataGridExportSupportsDllSchemaAsDtoOrReference(SmokeContext context)
    {
        RequireContains(context.CSharp, "public partial class OrderRow : ObservableObject", "DLL source should export portable DTO by schema.");
        RequireContains(context.CSharp, "SalesOrdersView", "DLL source should export stable collection binding.");
        RequireNotContains(context.CSharp, "MetadataLoadContext", "Generated project should not depend on designer-only DLL metadata packages.");
    }

    private static void ConfigureColumnEditorApplyUpdatesDataGridModel(MainWindowViewModel vm)
    {
        var source = ProductsSource();
        vm.BindingSources.Add(source);
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        var grid = DataGrid("ProductsGrid", source.Id, 32, 42, 520, 260);
        vm.Controls.Add(grid);
        vm.SelectControls(new[] { grid }, grid);

        var editor = new DataGridColumnEditorViewModel(vm, grid, source);
        editor.SelectedField!.Header = "Product title";
        editor.ApplyChanges();
        editor.Dispose();
    }

    private static void AssertColumnEditorApplyUpdatesDataGridModel(SmokeContext context)
    {
        var source = context.ViewModel.BindingSources.Single(source => source.Id == "products-source");
        if (source.Fields[0].Header != "Product title")
            throw new InvalidOperationException("Column Editor Apply should update BindingSource fields.");
        RequireContains(context.Xaml, "Header=\"Product title\"", "Column Editor Apply should affect export.");
    }

    private static void ConfigureColumnEditorCancelDoesNotModifyModel(MainWindowViewModel vm)
    {
        var source = ProductsSource();
        vm.BindingSources.Add(source);
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        var grid = DataGrid("ProductsGrid", source.Id, 32, 42, 520, 260);
        vm.Controls.Add(grid);
        vm.SelectControls(new[] { grid }, grid);

        var editor = new DataGridColumnEditorViewModel(vm, grid, source);
        editor.SelectedField!.Header = "Should not apply";
        editor.CancelChanges();
        editor.Dispose();
    }

    private static void AssertColumnEditorCancelDoesNotModifyModel(SmokeContext context)
    {
        var source = context.ViewModel.BindingSources.Single(source => source.Id == "products-source");
        if (source.Fields[0].Header != "Title")
            throw new InvalidOperationException("Column Editor Cancel should not modify BindingSource fields.");
        RequireContains(context.Xaml, "Header=\"Title\"", "Cancelled column edit should not affect export.");
        RequireNotContains(context.Xaml, "Should not apply", "Cancelled column edit leaked into export.");
    }

    private static void ConfigureColumnEditorAddRemoveDuplicateWorks(MainWindowViewModel vm)
    {
        var source = ProductsSource();
        vm.BindingSources.Add(source);
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        var grid = DataGrid("ProductsGrid", source.Id, 32, 42, 520, 260);
        vm.Controls.Add(grid);
        vm.SelectControls(new[] { grid }, grid);

        var editor = new DataGridColumnEditorViewModel(vm, grid, source);
        editor.AddColumn();
        editor.DuplicateSelectedColumn();
        editor.DeleteSelectedColumn();
        editor.ApplyChanges();
        editor.Dispose();
    }

    private static void AssertColumnEditorAddRemoveDuplicateWorks(SmokeContext context)
    {
        var source = context.ViewModel.BindingSources.Single(source => source.Id == "products-source");
        if (source.Fields.Count != 4)
            throw new InvalidOperationException($"Column Editor add/duplicate/remove should leave four fields, got {source.Fields.Count}.");
        RequireContains(context.Xaml, "Field4", "Column Editor added field should export.");
    }

    private static void ConfigureColumnEditorReorderColumnsAffectsPreviewAndExport(MainWindowViewModel vm)
    {
        var source = ProductsSource();
        vm.BindingSources.Add(source);
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        var grid = DataGrid("ProductsGrid", source.Id, 32, 42, 520, 260);
        vm.Controls.Add(grid);
        vm.SelectControls(new[] { grid }, grid);

        var editor = new DataGridColumnEditorViewModel(vm, grid, source);
        editor.MoveSelectedColumn(1);
        editor.ApplyChanges();
        editor.Dispose();
    }

    private static void AssertColumnEditorReorderColumnsAffectsPreviewAndExport(SmokeContext context)
    {
        var titleIndex = context.Xaml.IndexOf("Header=\"Title\"", StringComparison.Ordinal);
        var priceIndex = context.Xaml.IndexOf("Header=\"Price\"", StringComparison.Ordinal);
        if (titleIndex < 0 || priceIndex < 0 || priceIndex > titleIndex)
            throw new InvalidOperationException("Column Editor reorder should place Price before Title in export.");
    }

    private static void ConfigureColumnEditorDoesNotCallApplyDocument(MainWindowViewModel vm)
    {
        var source = ProductsSource();
        vm.BindingSources.Add(source);
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        var grid = DataGrid("ProductsGrid", source.Id, 32, 42, 520, 260);
        vm.Controls.Add(grid);
        vm.SelectControls(new[] { grid }, grid);
        vm.InteractionTraceEntries.Clear();

        var editor = new DataGridColumnEditorViewModel(vm, grid, source);
        editor.SelectedField!.Header = "Updated";
        editor.ApplyChanges();
        editor.Dispose();
    }

    private static void AssertColumnEditorDoesNotCallApplyDocument(SmokeContext context)
    {
        if (context.ViewModel.InteractionTraceEntries.Any(entry => entry.EventName.Contains("ApplyDocument", StringComparison.OrdinalIgnoreCase)))
        {
            var trace = string.Join(" | ", context.ViewModel.InteractionTraceEntries.Select(entry => entry.Summary));
            throw new InvalidOperationException($"Column Editor should not call ApplyDocument. Trace: {trace}");
        }
    }

    private static void ConfigureColumnEditorDoesNotResetSelectedControl(MainWindowViewModel vm)
    {
        var source = ProductsSource();
        vm.BindingSources.Add(source);
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        var grid = DataGrid("ProductsGrid", source.Id, 32, 42, 520, 260);
        vm.Controls.Add(grid);
        vm.SelectControls(new[] { grid }, grid);

        var editor = new DataGridColumnEditorViewModel(vm, grid, source);
        editor.SelectedField!.Header = "Updated";
        editor.ApplyChanges();
        editor.Dispose();
    }

    private static void AssertColumnEditorDoesNotResetSelectedControl(SmokeContext context)
    {
        if (context.ViewModel.SelectedControl?.Name != "ProductsGrid")
            throw new InvalidOperationException("Column Editor Apply should not reset SelectedControl.");
    }

    private static void ConfigureDataModeShowsSelectedDataGridSourceOnly(MainWindowViewModel vm)
    {
        var products = ProductsSource();
        var orders = DllOrdersSource();
        vm.BindingSources.Add(products);
        vm.BindingSources.Add(orders);
        var grid1 = DataGrid("SharedGridName", products.Id, 32, 42, 520, 260);
        var grid2 = DataGrid("SharedGridName", orders.Id, 32, 340, 520, 260);
        vm.Controls.Add(grid1);
        vm.Controls.Add(grid2);
        vm.SelectControls(new[] { grid2 }, grid2);
    }

    private static void AssertDataModeShowsSelectedDataGridSourceOnly(SmokeContext context)
    {
        RequireContains(context.ViewModel.DataGridDataSetupSummary, "Orders", "Data mode should display the selected DataGrid source.");
        RequireNotContains(context.ViewModel.DataGridDataSetupSummary, "ProductsSource", "Data mode should not show the first/random source when another DataGrid is selected.");
    }

    private static void AssertDataModeDoesNotEditColumnsDirectly(SmokeContext context)
    {
        RequireContains(File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "MainWindow.axaml"), Encoding.UTF8), "Колонки DataGrid редактируются в Column Editor", "Data mode should redirect column editing to Column Editor.");
    }

    private static void AssertDataModeOpensColumnEditorForSelectedGrid(SmokeContext context)
    {
        var grid = context.ViewModel.SelectedControl!;
        var editor = new DataGridColumnEditorViewModel(context.ViewModel, grid, context.ViewModel.SelectedBindingSourceForControl);
        if (!editor.SourceIdentityText.Contains("Orders", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Data mode should open Column Editor for the selected DataGrid source.");
        editor.Dispose();
    }

    private static void ConfigureDataModeHandlesSameGridNamesAcrossForms(MainWindowViewModel vm)
    {
        var products = ProductsSource();
        vm.BindingSources.Add(products);
        var grid1 = DataGrid("OrdersGrid", products.Id, 32, 42, 520, 260);
        vm.Controls.Add(grid1);
        var form2 = vm.CreateNewForm();
        form2.Name = "Form2";
        var orders = DllOrdersSource();
        vm.BindingSources.Add(orders);
        var grid2 = DataGrid("OrdersGrid", orders.Id, 32, 42, 520, 260);
        vm.Controls.Add(grid2);
        vm.SelectControls(new[] { grid2 }, grid2);
    }

    private static void AssertDataModeHandlesSameGridNamesAcrossForms(SmokeContext context)
    {
        RequireContains(context.ViewModel.DataGridDataSetupSummary, "Orders", "Data mode should resolve selected grid by active document/control, not by duplicate name.");
    }

    private static void ConfigureLinqToSqlDllImport(MainWindowViewModel vm)
    {
        var assemblyPath = CreateLinqToSqlMetadataAssembly(
            "SmokeLinqCustomers",
            "Smoke.Data.Customers",
            "Customer",
            "dbo.Customers");
        var imported = vm.ImportBindingSourcesFromAssembly(assemblyPath);
        if (imported <= 0)
            throw new InvalidOperationException("Expected LINQ to SQL smoke DLL to import at least one source.");
    }

    private static void AssertDllImportDetectsLinqToSqlTableAttribute(SmokeContext context)
    {
        var source = context.ViewModel.BindingSources.Single(source => source.SourceTypeFullName == "Smoke.Data.Customers.Customer");
        if (!string.Equals(source.SourceKind, "DllTable", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Expected source kind DllTable, got {source.SourceKind}.");

        if (!string.Equals(source.SourceTableName, "dbo.Customers", StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected TableAttribute name dbo.Customers, got {source.SourceTableName}.");

        var dll = context.ViewModel.ImportedDllCatalog.Single();
        if (!dll.HasTables || dll.TableCount < 1)
            throw new InvalidOperationException("Loaded DLL catalog should expose table metadata.");

        if (!dll.Tables.Any(table => table.IsLinqToSqlTable && table.TableName == "dbo.Customers"))
            throw new InvalidOperationException("DLL catalog should mark LINQ to SQL table metadata.");
    }

    private static void AssertDllImportDetectsLinqToSqlColumnAttributes(SmokeContext context)
    {
        var source = context.ViewModel.BindingSources.Single(source => source.SourceTypeFullName == "Smoke.Data.Customers.Customer");
        var id = source.Fields.Single(field => field.Path == "Id");
        var email = source.Fields.Single(field => field.Path == "Email");

        if (id.Header != "CustomerId" || !id.IsPrimaryKey || id.IsNullable || id.DbType != "Int NOT NULL")
            throw new InvalidOperationException($"Id column metadata was not imported correctly: {id.Header}, pk={id.IsPrimaryKey}, nullable={id.IsNullable}, db={id.DbType}.");

        if (email.Header != "EmailAddress" || email.DbType != "NVarChar(256)" || !email.IsNullable)
            throw new InvalidOperationException("Email column metadata was not imported correctly.");

        var table = context.ViewModel.ImportedDllCatalog.Single().Tables.Single(table => table.TableName == "dbo.Customers");
        if (!table.Columns.Any(column => column.ColumnName == "CustomerId" && column.IsPrimaryKey && !column.IsNullable))
            throw new InvalidOperationException("DLL UI metadata should include primary key column details.");
    }

    private static void ConfigureDuplicateTableDllImport(MainWindowViewModel vm)
    {
        var first = CreateLinqToSqlMetadataAssembly(
            "SmokeLinqCustomersA",
            "Smoke.Data.First",
            "Customer",
            "dbo.Customers");
        var second = CreateLinqToSqlMetadataAssembly(
            "SmokeLinqCustomersB",
            "Smoke.Data.Second",
            "Customer",
            "dbo.Customers");

        vm.ImportBindingSourcesFromAssembly(first);
        vm.ImportBindingSourcesFromAssembly(second);
    }

    private static void AssertDllImportSupportsDuplicateTableNamesAcrossDlls(SmokeContext context)
    {
        if (context.ViewModel.BindingSources.Count(source => source.SourceTableName == "dbo.Customers") != 2)
            throw new InvalidOperationException("Duplicate table names from different DLLs should import as separate sources.");

        var sourceKeys = context.ViewModel.ImportedDllCatalog
            .SelectMany(dll => dll.Tables.Select(table => table.SourceKey))
            .ToList();
        if (sourceKeys.Count != 2 || sourceKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 2)
            throw new InvalidOperationException("Duplicate table names should have distinct source keys.");

        if (!context.ViewModel.ImportedDllCatalog.Any(dll => dll.Tables.Any(table => table.DisplayName.Contains("SmokeLinqCustomersA", StringComparison.OrdinalIgnoreCase)))
            || !context.ViewModel.ImportedDllCatalog.Any(dll => dll.Tables.Any(table => table.DisplayName.Contains("SmokeLinqCustomersB", StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidOperationException("Duplicate table display names should be qualified by DLL name.");
        }
    }

    private static void AssertDllImportBuildsStableSourceKey(SmokeContext context)
    {
        var table = context.ViewModel.ImportedDllCatalog.Single().Tables.Single();
        RequireContains(table.SourceKey, "DLL|", "DLL source key should include source kind.");
        RequireContains(table.SourceKey, "SmokeLinqCustomers", "DLL source key should include assembly identity.");
        RequireContains(table.SourceKey, "Smoke.Data.Customers", "DLL source key should include namespace identity.");
        RequireContains(table.SourceKey, "Customer", "DLL source key should include type identity.");
        RequireContains(table.SourceKey, "dbo.Customers", "DLL source key should include table identity.");
    }

    private static void AssertDllSearchFindsTypeTableAndColumn(SmokeContext context)
    {
        var vm = context.ViewModel;
        vm.ImportedDllSearchText = "Customers";
        if (vm.FilteredImportedDllCatalog.Count != 1)
            throw new InvalidOperationException("DLL search should find by table name.");

        vm.ImportedDllSearchText = "Smoke.Data.Customers";
        if (vm.FilteredImportedDllCatalog.Count != 1)
            throw new InvalidOperationException("DLL search should find by namespace.");

        vm.ImportedDllSearchText = "EmailAddress";
        if (vm.FilteredImportedDllCatalog.Count != 1)
            throw new InvalidOperationException("DLL search should find by column name.");
    }

    private static void AssertLoadedDllPanelShowsCounts(SmokeContext context)
    {
        var dll = context.ViewModel.ImportedDllCatalog.Single();
        if (dll.TypeCount <= 0 || dll.TableCount <= 0 || dll.ColumnCount <= 0)
            throw new InvalidOperationException($"DLL card counts should be populated. {dll.CountsSummary}");

        if (dll.SourceNames.Contains(",", StringComparison.Ordinal) && dll.Tables.Count == 0)
            throw new InvalidOperationException("DLL card should not be only comma text without table nodes.");
    }

    private static void AssertRemoveDllRemovesMetadataAndSources(SmokeContext context)
    {
        var vm = context.ViewModel;
        var dll = vm.ImportedDllCatalog.Single();
        vm.RemoveImportedDll(dll);

        if (vm.ImportedDllCatalog.Count != 0 || vm.FilteredImportedDllCatalog.Count != 0 || vm.BindingSources.Count != 0)
            throw new InvalidOperationException("Remove DLL should clear metadata, filtered catalog and imported BindingSources.");
    }

    private static void AssertRemoveDllDetachesDataGridSourceWithoutCrash(SmokeContext context)
    {
        var vm = context.ViewModel;
        var source = vm.BindingSources.Single();
        var grid = DataGrid("CustomersGrid", source.Id, 32, 42, 520, 260);
        vm.Controls.Add(grid);
        vm.SelectControls(new[] { grid }, grid);

        var dll = vm.ImportedDllCatalog.Single();
        vm.RemoveImportedDll(dll);

        if (vm.ImportedDllCatalog.Count != 0)
            throw new InvalidOperationException("Removed DLL should disappear from catalog.");
        if (string.IsNullOrWhiteSpace(grid.BindingSourceId))
            throw new InvalidOperationException("DataGrid should be detached to a manual source instead of losing columns.");
        var detached = vm.BindingSources.SingleOrDefault(source => source.Id == grid.BindingSourceId)
            ?? throw new InvalidOperationException("Detached BindingSource was not created.");
        if (!detached.SourceKind.Contains("Detached", StringComparison.OrdinalIgnoreCase)
            || detached.SourceAssemblyPath.Length != 0
            || detached.Fields.Count == 0)
        {
            throw new InvalidOperationException("Detached BindingSource should keep columns and clear DLL assembly identity.");
        }
        if (!vm.SelectedDataGridBindingWarning.Contains("Источник данных удал", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("DataGrid should show a missing/deleted source warning after DLL removal.");
    }

    private static void ConfigureFailedDllImport(MainWindowViewModel vm)
    {
        var root = Path.Combine(Path.GetTempPath(), "FormDesignerSmokeDlls", "FailedDll");
        Directory.CreateDirectory(root);
        var badDllPath = Path.Combine(root, "NotARealAssembly.dll");
        File.WriteAllText(badDllPath, "this is not a managed assembly", Encoding.UTF8);
        vm.ImportBindingSourcesFromAssembly(badDllPath);
    }

    private static void AssertFailedDllCanBeRemoved(SmokeContext context)
    {
        var vm = context.ViewModel;
        var failed = vm.ImportedDllCatalog.SingleOrDefault()
            ?? throw new InvalidOperationException("Failed DLL should still be visible in the loaded DLL catalog.");
        if (!failed.IsFailed && !failed.HasErrors)
            throw new InvalidOperationException("Invalid DLL should be marked failed or errored.");
        vm.RemoveImportedDll(failed);
        if (vm.ImportedDllCatalog.Count != 0 || vm.FilteredImportedDllCatalog.Count != 0)
            throw new InvalidOperationException("Failed DLL entry should be removable.");
    }

    private static void AssertDllLoadFailureShowsErrorInUi(SmokeContext context)
    {
        var failed = context.ViewModel.ImportedDllCatalog.SingleOrDefault()
            ?? throw new InvalidOperationException("Failed DLL should produce a catalog item.");
        if (!failed.HasErrors || string.IsNullOrWhiteSpace(failed.ErrorMessage) || failed.Errors.Count == 0)
            throw new InvalidOperationException("Failed DLL should expose visible error message and details in UI model.");
        if (!failed.ErrorDetails.Contains("Exception", StringComparison.OrdinalIgnoreCase)
            && !failed.ErrorDetails.Contains("BadImageFormat", StringComparison.OrdinalIgnoreCase)
            && !failed.ErrorDetails.Contains("Metadata", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Failed DLL details should contain exception information. Details: {failed.ErrorDetails}");
        }
    }

    private static void AssertDuplicateTableNamesDisplayDllName(SmokeContext context)
    {
        var duplicateDisplays = context.ViewModel.ImportedDllCatalog
            .SelectMany(dll => dll.Tables.Select(table => table.DisplayName))
            .Where(name => name.Contains("dbo.Customers", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (duplicateDisplays.Count != 2)
            throw new InvalidOperationException("Expected two duplicate table display names.");
        if (duplicateDisplays.Any(name => name.EndsWith("1", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Duplicate table names should not be disambiguated with meaningless numeric suffixes.");
        if (!duplicateDisplays.Any(name => name.Contains("SmokeLinqCustomersA", StringComparison.OrdinalIgnoreCase))
            || !duplicateDisplays.Any(name => name.Contains("SmokeLinqCustomersB", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Duplicate table display names should include the DLL name.");
        }
    }

    private static void ConfigureManyTableDllImport(MainWindowViewModel vm)
    {
        for (var index = 0; index < 4; index++)
        {
            var assemblyPath = CreateManyTableMetadataAssembly($"SmokeManyTables{index}", $"Smoke.Many{index}", 30);
            vm.ImportBindingSourcesFromAssembly(assemblyPath);
        }
    }

    private static void AssertSearchManyTablesIsDebouncedAndFast(SmokeContext context)
    {
        var vm = context.ViewModel;
        var stopwatch = Stopwatch.StartNew();
        vm.ImportedDllSearchText = "Table29";
        stopwatch.Stop();
        if (vm.FilteredImportedDllCatalog.Count == 0)
            throw new InvalidOperationException("DLL search should find table names across large catalogs.");
        if (stopwatch.ElapsedMilliseconds > 750)
            throw new InvalidOperationException($"DLL search should stay fast for 100+ tables. Elapsed {stopwatch.ElapsedMilliseconds} ms.");
    }

    private static void AssertImportManyDllsDoesNotLoadPreviewRowsAutomatically(SmokeContext context)
    {
        var vm = context.ViewModel;
        if (vm.ImportedDllCatalog.Count != 4)
            throw new InvalidOperationException("Expected four imported DLL catalog cards.");
        if (vm.ImportedDllCatalog.Sum(dll => dll.TableCount) < 100)
            throw new InvalidOperationException("Many-table test should import at least 100 tables.");
        if (vm.SelectedBindingPreviewFields.Count > 6)
            throw new InvalidOperationException("DLL import should not load unlimited preview rows/fields automatically.");
    }

    private static void AssertRemoveDllReleasesMetadataAndCaches(SmokeContext context)
    {
        var vm = context.ViewModel;
        var dll = vm.ImportedDllCatalog.First();
        var removedPath = dll.AssemblyPath;
        vm.RemoveImportedDll(dll);
        if (vm.ImportedDllCatalog.Any(item => string.Equals(item.AssemblyPath, removedPath, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Removed DLL should not remain in catalog/search cache.");
        if (vm.BindingSources.Any(source => string.Equals(source.SourceAssemblyPath, removedPath, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Removed DLL sources should not remain in BindingSources.");
        vm.ImportedDllSearchText = Path.GetFileNameWithoutExtension(removedPath);
        if (vm.FilteredImportedDllCatalog.Any(item => string.Equals(item.AssemblyPath, removedPath, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Removed DLL should not remain searchable.");
    }

    private static void AssertDataGridBindingUsesFullDllSourceKey(SmokeContext context)
    {
        var source = context.ViewModel.BindingSources.Single();
        var grid = DataGrid("CustomersGrid", source.Id, 32, 42, 520, 260);
        context.ViewModel.Controls.Add(grid);
        context.ViewModel.SelectControls(new[] { grid }, grid);
        var table = context.ViewModel.ImportedDllCatalog.Single().Tables.Single();

        if (!table.SourceKey.Contains("SmokeLinqCustomers", StringComparison.OrdinalIgnoreCase)
            || !table.SourceKey.Contains("Smoke.Data.Customers", StringComparison.OrdinalIgnoreCase)
            || !table.SourceKey.Contains("dbo.Customers", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"DataGrid DLL binding should use full source key, got {table.SourceKey}.");
        }
    }

    private static void ConfigureDllTablePreviewRealRows(MainWindowViewModel vm)
    {
        var assemblyPath = CreateLinqToSqlPreviewRowsAssembly(
            "SmokePreviewOrders",
            "Smoke.Data.Preview",
            "OrderRow",
            "dbo.Orders",
            rowCount: 12);
        var imported = vm.ImportBindingSourcesFromAssembly(assemblyPath);
        if (imported <= 0)
            throw new InvalidOperationException("Expected preview rows DLL to import at least one source.");

        var source = vm.BindingSources.Single(source => source.SourceTypeFullName == "Smoke.Data.Preview.OrderRow");
        source.PreviewRowMode = BindingSourceModel.PreviewRowModeTopN;
        source.PreviewTopN = 50;
    }

    private static void AssertDllTablePreviewDoesNotUseFakeRowsWhenRealRowsAvailable(SmokeContext context)
    {
        var source = context.ViewModel.BindingSources.Single(source => source.SourceTypeFullName == "Smoke.Data.Preview.OrderRow");
        var rows = context.ViewModel.LoadPreviewRowsForBindingSourceAsync(source).GetAwaiter().GetResult();
        if (rows.Count != 12)
            throw new InvalidOperationException($"Expected 12 real DLL preview rows, got {rows.Count}.");

        if (!rows[0].TryGetValue("Name", out var firstName) || firstName != "Real order 001")
            throw new InvalidOperationException($"DLL preview should display real row values, got '{firstName}'.");

        if (string.Equals(source.PreviewRowsDataKind, "SampleRows", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Real DLL preview rows must not be marked as sample rows.");

        RequireTraceEvent(context, "DATAGRID_PREVIEW_REAL_ROWS_APPLIED");
    }

    private static void AssertDllTablePreviewClearlyMarksSampleRows(SmokeContext context)
    {
        var source = context.ViewModel.BindingSources.Single(source => source.SourceTypeFullName == "Smoke.Data.Customers.Customer");
        source.PreviewRowMode = BindingSourceModel.PreviewRowModeTopN;
        source.PreviewTopN = 10;

        var rows = context.ViewModel.LoadPreviewRowsForBindingSourceAsync(source).GetAwaiter().GetResult();
        if (rows.Count == 0)
            throw new InvalidOperationException("Sample fallback should still produce limited preview rows when enabled.");

        if (!string.Equals(source.PreviewRowsDataKind, "SampleRows", StringComparison.OrdinalIgnoreCase)
            || !source.PreviewRowsStatus.Contains("demo rows", StringComparison.OrdinalIgnoreCase)
            || !source.PreviewRowsStatus.Contains("Причина", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Sample fallback should be clearly marked. DataKind={source.PreviewRowsDataKind}; Status={source.PreviewRowsStatus}");
        }
    }

    private static void ConfigureDllTablePreviewTopN(MainWindowViewModel vm)
    {
        ConfigureDllTablePreviewRealRows(vm);
        var source = vm.BindingSources.Single(source => source.SourceTypeFullName == "Smoke.Data.Preview.OrderRow");
        source.PreviewTopN = 5;
    }

    private static void AssertDllTablePreviewTopNWorks(SmokeContext context)
    {
        var source = context.ViewModel.BindingSources.Single(source => source.SourceTypeFullName == "Smoke.Data.Preview.OrderRow");
        var rows = context.ViewModel.LoadPreviewRowsForBindingSourceAsync(source).GetAwaiter().GetResult();
        if (rows.Count != 5)
            throw new InvalidOperationException($"TopN should limit DLL preview rows to 5, got {rows.Count}.");
    }

    private static void ConfigureDllTablePreviewSortThenTopN(MainWindowViewModel vm)
    {
        ConfigureDllTablePreviewRealRows(vm);
        var source = vm.BindingSources.Single(source => source.SourceTypeFullName == "Smoke.Data.Preview.OrderRow");
        source.PreviewSortColumn = "CreatedAt";
        source.PreviewSortDirection = BindingFieldModel.SortDirectionDescending;
        source.PreviewTopN = 3;
    }

    private static void AssertDllTablePreviewSortThenTopNWorks(SmokeContext context)
    {
        var source = context.ViewModel.BindingSources.Single(source => source.SourceTypeFullName == "Smoke.Data.Preview.OrderRow");
        var rows = context.ViewModel.LoadPreviewRowsForBindingSourceAsync(source).GetAwaiter().GetResult();
        if (rows.Count != 3)
            throw new InvalidOperationException($"Sort + TopN should return 3 rows, got {rows.Count}.");

        if (!rows[0].TryGetValue("Name", out var firstName) || firstName != "Real order 012")
            throw new InvalidOperationException($"Sort should be applied before TopN. First row: {firstName}");
    }

    private static void AssertGeneratedNugetConfigContainsAllowInsecureConnectionsForHttpSource(SmokeContext context)
    {
        var nugetConfig = ExportPipelineService.BuildNuGetConfigForSources(new[]
        {
            new NuGetPackageSource("LocalHttp", "http://nuget.local/v3/index.json")
        });

        RequireContains(nugetConfig, "value=\"http://nuget.local/v3/index.json\"", "Generated NuGet.config should preserve HTTP source URL.");
        RequireContains(nugetConfig, "allowInsecureConnections=\"true\"", "HTTP NuGet source should explicitly opt into insecure connections.");
        RequireContains(nugetConfig, "https://api.nuget.org/v3/index.json", "Generated NuGet.config should still include nuget.org.");
    }

    private static void AssertExportGeneratesNugetConfigInProjectRoot(SmokeContext context)
    {
        using var export = ExportToTemporaryProject(context);
        RequireFileExists(Path.Combine(export.Path, "NuGet.config"), "Exported project root should contain NuGet.config.");
        if (Directory.GetFiles(export.Path, "*.csproj").Length != 1)
            throw new InvalidOperationException("Exported project root should contain exactly one .csproj.");
        RequireFileExists(Path.Combine(export.Path, "App.axaml"), "Exported project should include App.axaml.");
        RequireFileExists(Path.Combine(export.Path, "Program.cs"), "Exported project should include Program.cs.");
    }

    private static void AssertExportedNugetConfigContainsNugetOrgByDefault(SmokeContext context)
    {
        context.ViewModel.UseCustomNuGetSource = false;
        context.ViewModel.CustomNuGetSource = "";
        using var export = ExportToTemporaryProject(context);
        var nugetConfig = File.ReadAllText(Path.Combine(export.Path, "NuGet.config"), Encoding.UTF8);
        RequireContains(nugetConfig, "https://api.nuget.org/v3/index.json", "Exported NuGet.config should contain nuget.org by default.");
        RequireContains(nugetConfig, "<packageSourceMapping>", "Exported NuGet.config should override user package source mapping.");
        RequireContains(nugetConfig, "<clear />", "Exported NuGet.config should clear inherited package source mapping.");
    }

    private static void AssertExportedNugetConfigContainsCustomSource(SmokeContext context)
    {
        context.ViewModel.UseCustomNuGetSource = true;
        context.ViewModel.CustomNuGetSource = "https://example.com/nuget/v3/index.json";
        context.ViewModel.AllowInsecureNuGetSource = false;
        context.ViewModel.IncludeNuGetOrgFallback = true;
        using var export = ExportToTemporaryProject(context);
        var nugetConfig = File.ReadAllText(Path.Combine(export.Path, "NuGet.config"), Encoding.UTF8);
        RequireContains(nugetConfig, "https://example.com/nuget/v3/index.json", "Exported NuGet.config should contain custom source.");
        RequireContains(nugetConfig, "https://api.nuget.org/v3/index.json", "Exported NuGet.config should include nuget.org fallback when enabled.");
    }

    private static void AssertExportedNugetConfigSupportsHttpAllowInsecure(SmokeContext context)
    {
        context.ViewModel.UseCustomNuGetSource = true;
        context.ViewModel.CustomNuGetSource = "http://nuget.local/v3/index.json";
        context.ViewModel.AllowInsecureNuGetSource = true;
        context.ViewModel.IncludeNuGetOrgFallback = true;
        using var export = ExportToTemporaryProject(context);
        var nugetConfig = File.ReadAllText(Path.Combine(export.Path, "NuGet.config"), Encoding.UTF8);
        RequireContains(nugetConfig, "value=\"http://nuget.local/v3/index.json\"", "HTTP custom source should be written to exported NuGet.config.");
        RequireContains(nugetConfig, "allowInsecureConnections=\"true\"", "HTTP custom source should opt into allowInsecureConnections.");
    }

    private static void AssertDotnetRestoreWorksFromExportedProjectRoot(SmokeContext context)
    {
        context.ViewModel.UseCustomNuGetSource = false;
        context.ViewModel.CustomNuGetSource = "";
        using var export = ExportToTemporaryProject(context);
        var restore = RunProcess("dotnet", "restore", export.Path);
        if (restore.ExitCode != 0)
            throw new InvalidOperationException($"dotnet restore should work from exported project root.{Environment.NewLine}{restore.Output}");
    }

    private static void AssertValidateBuildUsesSameNugetConfigAsExportedProject(SmokeContext context)
    {
        context.ViewModel.UseCustomNuGetSource = true;
        context.ViewModel.CustomNuGetSource = "https://example.com/nuget/v3/index.json";
        context.ViewModel.AllowInsecureNuGetSource = false;
        context.ViewModel.IncludeNuGetOrgFallback = true;

        using var export = ExportToTemporaryProject(context);
        var exportedNugetConfig = NormalizeNugetConfigForCompare(File.ReadAllText(Path.Combine(export.Path, "NuGet.config"), Encoding.UTF8));
        var (result, messages) = RunValidateBuildForSmoke(context.ViewModel, context.Scenario.Name, requirePassed: false);
        var validationNugetConfig = NormalizeNugetConfigForCompare(File.ReadAllText(Path.Combine(result.ProjectPath, "NuGet.config"), Encoding.UTF8));
        if (!string.Equals(exportedNugetConfig, validationNugetConfig, StringComparison.Ordinal))
            throw new InvalidOperationException("Validate Build should use the same generated NuGet.config content as exported project.");
        if (!messages.Any(message => message.Contains("VALIDATE_BUILD_RESTORE_COMMAND", StringComparison.Ordinal)
                                     && message.Contains("NuGet.config", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Validate Build should log restore command and NuGet.config path.");
        }
    }

    private static void AssertBuildOutputDeduplicatesRepeatedNet6Warning(SmokeContext context)
    {
        const string warning = "NETSDK1138: The target framework 'net6.0' is out of support";
        var output = ExportPipelineService.DeduplicateBuildOutput($"{warning}{Environment.NewLine}{warning}{Environment.NewLine}restore ok");
        RequireContains(output, "[x2] NETSDK1138", "Repeated build warnings should be collapsed with a count.");
        if (output.Split("NETSDK1138", StringSplitOptions.None).Length != 2)
            throw new InvalidOperationException("Repeated NETSDK warning should appear once after deduplication.");
    }

    private static void AssertArtifactsCleanupDeletesOldExportRuns(SmokeContext context)
    {
        var fakeRepositoryRoot = Path.Combine(context.ProjectPath, "cleanup-root");
        var exportRoot = Path.Combine(fakeRepositoryRoot, "artifacts", "export");
        Directory.CreateDirectory(exportRoot);

        for (var index = 0; index < 7; index++)
        {
            var run = Path.Combine(exportRoot, $"20250101-00000{index}");
            Directory.CreateDirectory(run);
            File.WriteAllText(Path.Combine(run, "generated.txt"), index.ToString(), Encoding.UTF8);
            Directory.SetLastWriteTimeUtc(run, DateTime.UtcNow.AddMinutes(-index));
        }

        var cleanup = new ArtifactCleanupService().Clean(fakeRepositoryRoot, keepLatestRuns: 3, maxAge: TimeSpan.FromDays(365));
        var remainingRuns = Directory.GetDirectories(exportRoot).Length;
        if (remainingRuns != 3)
            throw new InvalidOperationException($"Artifact cleanup should keep three latest export runs, got {remainingRuns}.");
        if (cleanup.DirectoriesDeleted < 4)
            throw new InvalidOperationException("Artifact cleanup should delete stale export run folders.");
    }

    private static void AssertExportGeneratesAtMostOneReadme(SmokeContext context)
    {
        var exportFolder = Path.Combine(Path.GetTempPath(), "FormDesignerSmokeExports", Guid.NewGuid().ToString("N"));
        var service = new ExportPipelineService();
        var result = service.CreateResult(
            new ExportProfile
            {
                ProjectNamespace = context.ViewModel.ExportProjectNamespace,
                TargetMode = context.ViewModel.ExportTarget,
                DataGridExportMode = context.ViewModel.DataGridExportMode,
                LayoutExportMode = context.ViewModel.LayoutExportMode
            },
            context.GeneratedFiles,
            context.ViewModel.RequiredPackages,
            context.ViewModel.ExportDiagnostics);

        try
        {
            service.ExportToProjectAsync(result, exportFolder).GetAwaiter().GetResult();
            var readmeCount = Directory.GetFiles(exportFolder, "README*.md", SearchOption.AllDirectories).Length;
            if (readmeCount != 1)
                throw new InvalidOperationException($"Export should write exactly one generated README, got {readmeCount}.");

            var readme = File.ReadAllText(Path.Combine(exportFolder, "README.generated.md"), Encoding.UTF8);
            RequireContains(readme, "dotnet restore", "Generated README should include restore instructions.");
        RequireContains(readme, "11.1.5", "Generated README should document Avalonia version.");
        }
        finally
        {
            if (Directory.Exists(exportFolder))
                Directory.Delete(exportFolder, recursive: true);
        }
    }

    private static void AssertGeneratedSolutionHasConsistentAvaloniaVersions(SmokeContext context)
    {
        var projectFile = File.ReadAllText(Path.Combine(context.ProjectPath, $"{context.Scenario.Name}.csproj"), Encoding.UTF8);
        RequireContains(projectFile, "Avalonia\" Version=\"11.1.5\"", "Generated solution should use Avalonia 11.1.5.");
        RequireContains(projectFile, "Avalonia.Desktop\" Version=\"11.1.5\"", "Generated solution should use Avalonia.Desktop 11.1.5.");
        RequireContains(projectFile, "Avalonia.Controls.DataGrid\" Version=\"11.1.5\"", "Generated solution should use DataGrid 11.1.5.");
        RequireNotContains(projectFile, "11.3.11", "Generated solution should not mix Avalonia 11.3.11 packages.");
    }

    private static void AssertGeneratedSolutionDoesNotIncludeDesignerOnlyPackages(SmokeContext context)
    {
        var projectFile = File.ReadAllText(Path.Combine(context.ProjectPath, $"{context.Scenario.Name}.csproj"), Encoding.UTF8);
        RequireNotContains(projectFile, "System.Reflection.MetadataLoadContext", "Generated runtime project should not include designer-only MetadataLoadContext.");
        RequireNotContains(projectFile, "Avalonia.Controls.ColorPicker", "Generated runtime project should not include designer-only ColorPicker package.");
        RequireNotContains(projectFile, "Avalonia.Diagnostics", "Generated runtime project should not include designer diagnostics package.");
    }

    private static void ConfigureLogicTemplateEditor(MainWindowViewModel vm)
    {
        ConfigureDataGridSelectionToTextBoxExport(vm);
        vm.SelectedInteraction = vm.Interactions.Single(interaction =>
            string.Equals(interaction.SourceControlName, "ProductsGrid", StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertLogicTemplateTypingDoesNotCallApplyDocument(SmokeContext context)
    {
        var vm = context.ViewModel;
        vm.InteractionTraceEntries.Clear();
        vm.LogicTemplateDraft = "ID: {Title}";
        vm.LogicTemplateDraft += Environment.NewLine + "Price: {Price}";

        var forbidden = vm.InteractionTraceEntries
            .Where(entry => entry.EventName.Contains("ApplyDocument", StringComparison.OrdinalIgnoreCase)
                            || entry.EventName.Contains("APPLY_DOCUMENT", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (forbidden.Count > 0)
            throw new InvalidOperationException($"Typing a Logic template must not call ApplyDocument: {string.Join(" | ", forbidden.Select(entry => entry.Summary))}");
    }

    private static void AssertLogicTemplateTypingDoesNotRebuildPropertyGrid(SmokeContext context)
    {
        var vm = context.ViewModel;
        vm.InteractionTraceEntries.Clear();
        vm.LogicTemplateDraft = "Title: {Title}";
        vm.LogicTemplateDraft += " / Count: {Count}";

        var forbidden = vm.InteractionTraceEntries
            .Where(entry => entry.EventName.Contains("RebuildPropertyGrid", StringComparison.OrdinalIgnoreCase)
                            || entry.EventName.Contains("BuildGeneratedFiles", StringComparison.OrdinalIgnoreCase)
                            || entry.EventName.Contains("RefreshExportPipelineResult", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (forbidden.Count > 0)
            throw new InvalidOperationException($"Typing a Logic template must stay local: {string.Join(" | ", forbidden.Select(entry => entry.Summary))}");
    }

    private static void AssertLogicTemplateApplyUpdatesModel(SmokeContext context)
    {
        var vm = context.ViewModel;
        var interaction = vm.SelectedInteraction ?? throw new InvalidOperationException("Logic template test interaction was not selected.");
        vm.LogicTemplateDraft = "Product: {Title}";
        if (string.Equals(interaction.TextTemplate, vm.LogicTemplateDraft, StringComparison.Ordinal))
            throw new InvalidOperationException("Typing a Logic template should not update the model before Apply.");

        vm.ApplyLogicTemplateEditCommand.Execute(null);
        if (!string.Equals(interaction.TextTemplate, "Product: {Title}", StringComparison.Ordinal))
            throw new InvalidOperationException("Apply should copy the draft template into the interaction model.");
    }

    private static void AssertLogicTemplateCancelDoesNotModifyModel(SmokeContext context)
    {
        var vm = context.ViewModel;
        var interaction = vm.SelectedInteraction ?? throw new InvalidOperationException("Logic template test interaction was not selected.");
        var original = interaction.TextTemplate;
        vm.LogicTemplateDraft = "Should not persist {Price}";
        vm.CancelLogicTemplateEditCommand.Execute(null);

        if (!string.Equals(interaction.TextTemplate, original, StringComparison.Ordinal))
            throw new InvalidOperationException("Cancel should leave the interaction model unchanged.");
        if (!string.Equals(vm.LogicTemplateDraft, original, StringComparison.Ordinal))
            throw new InvalidOperationException("Cancel should restore the draft from the interaction model.");
    }

    private static void ConfigureDataGridSelectionToTextBoxExport(MainWindowViewModel vm)
    {
        var source = ProductsSource();
        vm.BindingSources.Add(source);
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        vm.Controls.Add(DataGrid("ProductsGrid", source.Id, 32, 36, 520, 250));
        vm.Controls.Add(Control(DesignerControlTypes.TextBox, "SelectedTitleTextBox", 590, 44, 260, 38, placeholder: "Selected title"));
        vm.Interactions.Add(new InteractionModel
        {
            SourceControlName = "ProductsGrid",
            EventName = InteractionModel.EventDataGridSelectionChanged,
            ActionType = InteractionModel.ActionSetProperty,
            TargetControlName = "SelectedTitleTextBox",
            TargetProperty = InteractionModel.TargetPropertyText,
            SourcePath = "Title",
            TextTemplate = "Title: {Title}\nPrice: {Price}",
            MissingValueBehavior = InteractionModel.MissingValueKeepPlaceholder,
            NoSelectionBehavior = InteractionModel.NoSelectionClearTarget
        });
    }

    private static void AssertExportDataGridSelectionToTextBoxBuilds(SmokeContext context)
    {
        RequireContains(context.Xaml, "SelectionChanged=\"ProductsGrid_SelectionChanged\"", "DataGrid selected-row action should wire SelectionChanged.");
        RequireContains(context.CSharp, "private void ProductsGrid_SelectionChanged", "DataGrid selected-row action should generate a handler.");
        RequireContains(context.CSharp, "SelectedTitleTextBox.Text = ResolveInteractionValue(selectedItem", "Selected row should write to the target TextBox.Text.");
        RequireContains(context.CSharp, "Title: {Title}", "Generated handler should include the configured template.");
        RequireContains(context.CSharp, "@\"KeepPlaceholder\"", "Generated handler should preserve the missing-value behavior.");
    }

    private static void ConfigureDataGridSelectionToTextBlockExport(MainWindowViewModel vm)
    {
        var source = ProductsSource();
        vm.BindingSources.Add(source);
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        vm.Controls.Add(DataGrid("ProductsGrid", source.Id, 32, 36, 520, 250));
        vm.Controls.Add(Control(DesignerControlTypes.TextBlock, "SelectedTitleTextBlock", 590, 44, 300, 80, text: "Selected row"));
        vm.Interactions.Add(new InteractionModel
        {
            SourceControlName = "ProductsGrid",
            EventName = InteractionModel.EventDataGridSelectionChanged,
            ActionType = InteractionModel.ActionSetProperty,
            TargetControlName = "SelectedTitleTextBlock",
            TargetProperty = InteractionModel.TargetPropertyText,
            SourcePath = "Title",
            TextTemplate = "Title: {Title}\nCount: {Count}",
            MissingValueBehavior = InteractionModel.MissingValueEmpty,
            NoSelectionBehavior = InteractionModel.NoSelectionSetText,
            NoSelectionText = "No row"
        });
    }

    private static void AssertExportDataGridSelectionToTextBlockBuilds(SmokeContext context)
    {
        RequireContains(context.Xaml, "SelectionChanged=\"ProductsGrid_SelectionChanged\"", "DataGrid selected-row TextBlock action should wire SelectionChanged.");
        RequireContains(context.CSharp, "SelectedTitleTextBlock.Text =", "Selected row should write to the target TextBlock.Text.");
        RequireContains(context.CSharp, "@\"No row\"", "Generated handler should include configured no-selection text.");
        RequireContains(context.CSharp, "Title: {Title}", "Generated handler should include the TextBlock template.");
    }

    private static void ConfigureDataGridSelectionInvalidPlaceholder(MainWindowViewModel vm)
    {
        ConfigureDataGridSelectionToTextBoxExport(vm);
        vm.Interactions[0].TextTemplate = "Unknown: {DoesNotExist}";
    }

    private static void AssertExportLogicInvalidPlaceholderShowsWarning(SmokeContext context)
    {
        RequireContains(context.DiagnosticsText, "{DoesNotExist}", "Invalid Logic template placeholders should be reported before export.");
        RequireContains(context.DiagnosticsText, "BindingSource", "Invalid placeholder diagnostics should name the DataGrid BindingSource context.");
    }

    private static void AssertExportLogicUsesGeneratedRowDtoProperties(SmokeContext context)
    {
        RequireContains(context.CSharp, "public partial class ProductRow : ObservableObject", "DataGrid selected-row export should keep the generated row DTO.");
        RequireContains(context.CSharp, "private string title;", "Generated DTO should include the Title property used by the template.");
        RequireContains(context.CSharp, "private decimal price;", "Generated DTO should include the Price property used by the template.");
        RequireContains(context.CSharp, "private static string ResolveInteractionValue", "Generated interaction code should use the shared template resolver.");
    }

    private static void ConfigureInteractionsExport(MainWindowViewModel vm)
    {
        var source = ProductsSource();
        vm.BindingSources.Add(source);
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;

        vm.Controls.Add(DataGrid("ProductsGrid", source.Id, 32, 36, 520, 250));
        vm.Controls.Add(Control(DesignerControlTypes.TextBox, "SelectedTitleTextBox", 590, 44, 240, 38, placeholder: "Selected title"));
        vm.Controls.Add(Control(DesignerControlTypes.CheckBox, "DetailsCheckBox", 590, 100, 180, 34, text: "Show details"));
        vm.Controls.Add(Control(DesignerControlTypes.Border, "DetailsPanel", 590, 150, 240, 90, background: "#EFF6FF", border: "#BFDBFE", radius: 12));
        vm.Controls.Add(Control(DesignerControlTypes.Button, "MessageButton", 590, 270, 180, 42, text: "Show message", background: "#2563EB", foreground: "#FFFFFF", border: "#1D4ED8", radius: 10));

        vm.Interactions.Add(new InteractionModel
        {
            SourceControlName = "MessageButton",
            EventName = InteractionModel.EventButtonClick,
            ActionType = InteractionModel.ActionShowMessage,
            TextTemplate = "Smoke message",
            MessageTitle = "Smoke"
        });
        vm.Interactions.Add(new InteractionModel
        {
            SourceControlName = "DetailsCheckBox",
            EventName = InteractionModel.EventCheckBoxChecked,
            ActionType = InteractionModel.ActionToggleVisibility,
            TargetControlName = "DetailsPanel",
            TargetProperty = InteractionModel.TargetPropertyIsVisible
        });
        vm.Interactions.Add(new InteractionModel
        {
            SourceControlName = "DetailsCheckBox",
            EventName = InteractionModel.EventCheckBoxUnchecked,
            ActionType = InteractionModel.ActionToggleVisibility,
            TargetControlName = "DetailsPanel",
            TargetProperty = InteractionModel.TargetPropertyIsVisible
        });
        vm.Interactions.Add(new InteractionModel
        {
            SourceControlName = "ProductsGrid",
            EventName = InteractionModel.EventDataGridSelectionChanged,
            ActionType = InteractionModel.ActionSetProperty,
            TargetControlName = "SelectedTitleTextBox",
            TargetProperty = InteractionModel.TargetPropertyText,
            SourcePath = "Title"
        });
    }

    private static void AssertInteractionsExport(SmokeContext context)
    {
        RequireContains(context.Xaml, "Click=\"MessageButtonClick\"", "Button click handler missing.");
        RequireContains(context.Xaml, "Checked=\"DetailsCheckBox_Checked\"", "CheckBox checked handler missing.");
        RequireContains(context.Xaml, "Unchecked=\"DetailsCheckBox_Unchecked\"", "CheckBox unchecked handler missing.");
        RequireContains(context.Xaml, "SelectionChanged=\"ProductsGrid_SelectionChanged\"", "DataGrid selection handler missing.");
        RequireContains(context.CSharp, "private async void MessageButtonClick", "ShowMessage handler missing.");
        RequireContains(context.CSharp, "private void DetailsCheckBox_Checked", "Checked handler missing.");
        RequireContains(context.CSharp, "private void DetailsCheckBox_Unchecked", "Unchecked handler missing.");
        RequireContains(context.CSharp, "private void ProductsGrid_SelectionChanged", "SelectionChanged handler missing.");
        RequireContains(context.CSharp, "ShowMessageAsync", "ShowMessage helper missing.");
        RequireContains(context.CSharp, "ObservableCollection<ProductRow>", "Real DataGrid interactions should keep generated runtime binding classes.");
    }

    private static void ConfigureMultiFormOpenFormExport(MainWindowViewModel vm)
    {
        vm.ExportTarget = MainWindowViewModel.ExportTargetMainWindow;
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeVisual;
        vm.Controls.Add(Control(DesignerControlTypes.TextBlock, "Form1Title", 36, 34, 340, 30, text: "Form1"));
        vm.Controls.Add(Control(DesignerControlTypes.Button, "OpenForm2Button", 36, 92, 180, 42, text: "Open Form2", background: "#2563EB", foreground: "#FFFFFF", border: "#1D4ED8", radius: 10));

        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Active Form1 document missing.");
        vm.NewFormEditorCommand?.Execute(null);
        var form2 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Active Form2 document missing.");
        form2.Name = "Form2";
        form2.Document.FormTitle = "Form2";
        vm.FormTitle = "Form2";
        vm.Controls.Add(Control(DesignerControlTypes.TextBlock, "Form2Title", 36, 34, 340, 30, text: "Form2 window"));

        vm.NewFormEditorCommand?.Execute(null);
        var form3 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Active Form3 document missing.");
        form3.Name = "Form3";
        form3.Document.FormTitle = "Form3";
        vm.FormTitle = "Form3";
        vm.Controls.Add(Control(DesignerControlTypes.TextBox, "Form3TextBox", 36, 84, 260, 40, placeholder: "Form3 text"));

        SwitchToForm(vm, form1.Id);
        RequireActiveControls(vm, "Form1Title", "OpenForm2Button");
        RequireNoActiveControls(vm, "Form2Title", "Form3TextBox");

        SwitchToForm(vm, form2.Id);
        RequireActiveControls(vm, "Form2Title");
        RequireNoActiveControls(vm, "Form1Title", "OpenForm2Button", "Form3TextBox");

        SwitchToForm(vm, form3.Id);
        RequireActiveControls(vm, "Form3TextBox");
        RequireNoActiveControls(vm, "Form1Title", "OpenForm2Button", "Form2Title");

        SwitchToForm(vm, form1.Id);
        vm.Interactions.Add(new InteractionModel
        {
            SourceControlName = "OpenForm2Button",
            EventName = InteractionModel.EventButtonClick,
            ActionType = InteractionModel.ActionOpenForm,
            TargetFormId = form2.Id,
            TargetFormName = form2.DisplayName,
            OpenMode = InteractionModel.OpenModeShow
        });
    }

    private static void AssertMultiFormOpenFormExport(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml");
        RequireGeneratedFile(context, "MainWindow.axaml.cs");
        RequireGeneratedFile(context, "Form2.axaml");
        RequireGeneratedFile(context, "Form2.axaml.cs");
        RequireGeneratedFile(context, "Form3.axaml");
        RequireGeneratedFile(context, "Form3.axaml.cs");
        RequireContains(context.Xaml, "Click=\"OpenForm2ButtonClick\"", "OpenForm Button.Click handler missing in Form1 XAML.");
        RequireContains(context.CSharp, "var windowForm2 = new Form2();", "OpenForm handler should create Form2.");
        RequireContains(context.CSharp, "windowForm2.Show();", "OpenForm handler should show Form2.");
        RequireContains(context.ChecklistText, "Forms exported: 3/3", "Export checklist should report all forms.");
        RequireContains(context.ChecklistText, "OpenForm interactions: 1", "Export checklist should report OpenForm interaction.");
    }

    private static void ConfigureMultiFormDocumentStateIsolation(MainWindowViewModel vm)
    {
        vm.Controls.Add(Control(DesignerControlTypes.Button, "Form1Button", 24, 42, 160, 38, text: "Form1 original"));
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");

        vm.NewFormEditorCommand?.Execute(null);
        var form2 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form2 missing.");
        form2.Name = "Form2";
        form2.Document.FormTitle = "Form2";
        vm.FormTitle = "Form2";
        vm.Controls.Add(Control(DesignerControlTypes.TextBox, "Form2TextBox", 24, 48, 240, 36, placeholder: "Form2 original"));

        SwitchToForm(vm, form1.Id);
        RequireActiveControls(vm, "Form1Button");
        RequireNoActiveControls(vm, "Form2TextBox");
        var form1Button = vm.Controls.Single(control => control.Name == "Form1Button");
        form1Button.Text = "Form1 edited";
        form1Button.Width = 210;

        SwitchToForm(vm, form2.Id);
        RequireActiveControls(vm, "Form2TextBox");
        RequireNoActiveControls(vm, "Form1Button");
        var form2TextBox = vm.Controls.Single(control => control.Name == "Form2TextBox");
        form2TextBox.PlaceholderText = "Form2 edited";
        form2TextBox.Width = 280;

        SwitchToForm(vm, form1.Id);
        RequireActiveControls(vm, "Form1Button");
        RequireNoActiveControls(vm, "Form2TextBox");
        var restoredForm1Button = vm.Controls.Single(control => control.Name == "Form1Button");
        if (restoredForm1Button.Text != "Form1 edited" || Math.Abs(restoredForm1Button.Width - 210) > 0.001)
            throw new InvalidOperationException("Form1 edited properties were not preserved after switching documents.");

        SwitchToForm(vm, form2.Id);
        RequireActiveControls(vm, "Form2TextBox");
        RequireNoActiveControls(vm, "Form1Button");
        var restoredForm2TextBox = vm.Controls.Single(control => control.Name == "Form2TextBox");
        if (restoredForm2TextBox.PlaceholderText != "Form2 edited" || Math.Abs(restoredForm2TextBox.Width - 280) > 0.001)
            throw new InvalidOperationException("Form2 edited properties were not preserved after switching documents.");

        SwitchToForm(vm, form1.Id);
    }

    private static void AssertMultiFormDocumentStateIsolation(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml");
        RequireGeneratedFile(context, "MainWindow.axaml.cs");
        RequireGeneratedFile(context, "Form2.axaml");
        RequireGeneratedFile(context, "Form2.axaml.cs");
        RequireContains(context.Xaml, "Form1 edited", "Form1 exported state should include edited button text.");
        RequireNotContains(context.Xaml, "Form2 edited", "Active Form1 export should not contain Form2 control state.");
        var form2File = context.GeneratedFiles.First(file => string.Equals(file.Path, "Form2.axaml", StringComparison.OrdinalIgnoreCase));
        RequireContains(form2File.Content, "Form2 edited", "Form2 exported state should include edited placeholder.");
        RequireNotContains(form2File.Content, "Form1 edited", "Form2 export should not contain Form1 control state.");
        RequireNotContains(context.DiagnosticsText, "Document isolation", "Valid multi-form switching should not produce isolation diagnostics.");
    }

    private static void ConfigureDesignerDocumentSessionCreatedForOpenedForm(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        if (!string.Equals(vm.ActiveSession.DocumentId, form1.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Active session was not created for Form1.");
        if (!vm.DocumentSessions.TryGetValue(form1.Id, out var form1Session)
            || !ReferenceEquals(form1Session, vm.ActiveSession))
        {
            throw new InvalidOperationException("Form1 session is not registered as the active session.");
        }

        var form2 = vm.CreateNewForm();
        if (!vm.DocumentSessions.TryGetValue(form2.Id, out var form2Session)
            || !ReferenceEquals(form2Session, vm.ActiveSession)
            || ReferenceEquals(form1Session, form2Session))
        {
            throw new InvalidOperationException("Opening Form2 did not create a distinct active document session.");
        }

        SwitchToForm(vm, form1.Id);
        if (!ReferenceEquals(vm.ActiveSession, form1Session))
            throw new InvalidOperationException("Returning to Form1 did not reactivate its existing session.");
    }

    private static void AssertDesignerDocumentSessionCreatedForOpenedForm(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml");
    }

    private static void ConfigureSelectionLivesInActiveDocumentSession(MainWindowViewModel vm)
    {
        var button = Control(DesignerControlTypes.Button, "SessionButton", 28, 34, 160, 38, text: "Session");
        vm.Controls.Add(button);
        vm.SelectSingleControl(button);

        if (!ReferenceEquals(vm.ActiveSession.SelectedControl, button)
            || !ReferenceEquals(vm.SelectedControl, button)
            || vm.ActiveSession.SelectedControlIds.Count != 1
            || !string.Equals(vm.ActiveSession.SelectedControlIds[0], button.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Selection is not owned by the active document session.");
        }

        vm.SelectedControl = null;
        if (vm.ActiveSession.SelectedControl is not null || vm.ActiveSession.SelectedControlIds.Count != 0)
            throw new InvalidOperationException("SelectedControl facade did not clear active session selection.");
    }

    private static void AssertSelectionLivesInActiveDocumentSession(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml");
    }

    private static void ConfigureSwitchingFormsSwitchesDocumentSession(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var form1Session = vm.ActiveSession;
        vm.Controls.Add(Control(DesignerControlTypes.Button, "Form1SessionButton", 28, 34, 160, 38, text: "Form1"));

        var form2 = vm.CreateNewForm();
        var form2Session = vm.ActiveSession;
        vm.Controls.Add(Control(DesignerControlTypes.TextBox, "Form2SessionText", 28, 34, 220, 38, placeholder: "Form2"));

        if (ReferenceEquals(form1Session, form2Session))
            throw new InvalidOperationException("Different forms share one DesignerDocumentSession.");

        SwitchToForm(vm, form1.Id);
        if (!ReferenceEquals(vm.ActiveSession, form1Session) || vm.Controls.Any(control => control.Name == "Form2SessionText"))
            throw new InvalidOperationException("Form switch did not restore Form1 session state.");

        SwitchToForm(vm, form2.Id);
        if (!ReferenceEquals(vm.ActiveSession, form2Session) || vm.Controls.Any(control => control.Name == "Form1SessionButton"))
            throw new InvalidOperationException("Form switch did not restore Form2 session state.");
    }

    private static void AssertSwitchingFormsSwitchesDocumentSession(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml");
    }

    private static void ConfigureAddFormDoesNotMutateExistingSession(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var form1Session = vm.ActiveSession;
        var button = Control(DesignerControlTypes.Button, "Form1SessionStable", 28, 34, 160, 38, text: "Stable");
        vm.Controls.Add(button);

        var form2 = vm.CreateNewForm();
        if (form1Session.IsDisposed || vm.DocumentSessions[form1.Id] != form1Session)
            throw new InvalidOperationException("Add Form disposed or replaced the existing Form1 session.");
        if (!form1Session.Document.Controls.Any(control => string.Equals(control.Id, button.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Add Form mutated Form1 persisted document state.");

        SwitchToForm(vm, form1.Id);
        if (!vm.Controls.Any(control => string.Equals(control.Id, button.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Form1 controls were lost after creating Form2.");

        SwitchToForm(vm, form2.Id);
    }

    private static void AssertAddFormDoesNotMutateExistingSession(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml");
    }

    private static void ConfigureDeletingFormDisposesItsSession(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var form2 = vm.CreateNewForm();
        var form2Session = vm.ActiveSession;
        var form2ExplorerItem = FlattenExplorerItems(vm.ProjectExplorerItems)
            .FirstOrDefault(item => item.Source is DesignerFormDocument form
                && string.Equals(form.Id, form2.Id, StringComparison.OrdinalIgnoreCase));
        if (form2ExplorerItem is null)
            throw new InvalidOperationException("Form2 is missing from Project Explorer.");

        vm.SelectedProjectExplorerItem = form2ExplorerItem;
        vm.DeleteFormEditorCommand?.Execute(null);

        if (!form2Session.IsDisposed || vm.DocumentSessions.ContainsKey(form2.Id))
            throw new InvalidOperationException("Deleting Form2 did not dispose and unregister its session.");
        if (!string.Equals(vm.ActiveDocumentId, form1.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Deleting active Form2 did not activate Form1.");
    }

    private static void AssertDeletingFormDisposesItsSession(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml");
    }

    private static void ConfigureNewProjectDisposesAllOldDocumentSessions(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var form1Session = vm.ActiveSession;
        var form2 = vm.CreateNewForm();
        var form2Session = vm.ActiveSession;

        vm.NewDocumentCommand.Execute(null);

        if (!form1Session.IsDisposed || !form2Session.IsDisposed)
            throw new InvalidOperationException("New Project did not dispose old document sessions.");
        if (vm.DocumentSessions.ContainsKey(form1.Id) || vm.DocumentSessions.ContainsKey(form2.Id))
            throw new InvalidOperationException("New Project retained old document sessions.");
        if (!string.Equals(vm.ActiveSession.DocumentId, vm.ActiveDocumentId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("New Project active session does not match the new Form1.");
    }

    private static void AssertNewProjectDisposesAllOldDocumentSessions(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml");
    }

    private static void ConfigurePropertyInspectorFollowsActiveSessionSelection(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = Control(DesignerControlTypes.Button, "InspectorSessionButton", 28, 34, 160, 38, text: "Form1");
        vm.Controls.Add(button);
        vm.SelectSingleControl(button);
        RequirePropertyGridContext(vm, form1.Id, button.Id);
        if (!ReferenceEquals(vm.ActiveSession.SelectedControl, button))
            throw new InvalidOperationException("Property Inspector target does not come from the active session selection.");

        var form2 = vm.CreateNewForm();
        var textBox = Control(DesignerControlTypes.TextBox, "InspectorSessionText", 28, 34, 220, 38, placeholder: "Form2");
        vm.Controls.Add(textBox);
        vm.SelectSingleControl(textBox);
        RequirePropertyGridContext(vm, form2.Id, textBox.Id);

        SwitchToForm(vm, form1.Id);
        var restoredButton = vm.Controls.Single(control => control.Name == "InspectorSessionButton");
        vm.SelectSingleControl(restoredButton);
        RequirePropertyGridContext(vm, form1.Id, restoredButton.Id);
    }

    private static void AssertPropertyInspectorFollowsActiveSessionSelection(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml");
    }

    private static void ConfigureUndoRedoIsIsolatedPerDocumentSession(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var form1Button = Control(DesignerControlTypes.Button, "UndoForm1Button", 28, 34, 160, 38, text: "Form1 original");
        vm.Controls.Add(form1Button);
        form1Button.Text = "Form1 changed";
        var form1Session = vm.ActiveSession;
        if (!form1Session.CanUndo)
            throw new InvalidOperationException("Form1 edit did not create document-scoped history.");

        var form2 = vm.CreateNewForm();
        var form2Button = Control(DesignerControlTypes.Button, "UndoForm2Button", 28, 34, 160, 38, text: "Form2 original");
        vm.Controls.Add(form2Button);
        form2Button.Text = "Form2 changed";
        var form2Session = vm.ActiveSession;
        if (!form2Session.CanUndo)
            throw new InvalidOperationException("Form2 edit did not create document-scoped history.");

        vm.UndoCommand.Execute(null);
        if (!ReferenceEquals(vm.ActiveSession, form2Session))
            throw new InvalidOperationException("Undo switched the active document session.");

        SwitchToForm(vm, form1.Id);
        var restoredForm1Button = vm.Controls.Single(control => control.Name == "UndoForm1Button");
        if (!string.Equals(restoredForm1Button.Text, "Form1 changed", StringComparison.Ordinal))
            throw new InvalidOperationException("Undo in Form2 changed Form1 state.");

        SwitchToForm(vm, form2.Id);
    }

    private static void AssertUndoRedoIsIsolatedPerDocumentSession(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml");
    }

    private static void ConfigureApplyDocumentPreservesSelectionThroughSession(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = Control(DesignerControlTypes.Button, "ApplySessionButton", 28, 34, 160, 38, text: "Before add Form");
        vm.Controls.Add(button);
        vm.SelectSingleControl(button);

        vm.CreateNewForm();
        SwitchToForm(vm, form1.Id);
        var restoredButton = vm.Controls.Single(control => control.Name == "ApplySessionButton");
        vm.SelectSingleControl(restoredButton);
        if (!ReferenceEquals(vm.ActiveSession.SelectedControl, restoredButton))
            throw new InvalidOperationException("Selection was not restored into the active session after ApplyDocument.");

        SetPropertyGridValue(vm, nameof(DesignControlModel.Text), "After add Form");
        if (!string.Equals(restoredButton.Text, "After add Form", StringComparison.Ordinal))
            throw new InvalidOperationException("Property Inspector edit was lost after ApplyDocument/session activation.");
    }

    private static void AssertApplyDocumentPreservesSelectionThroughSession(SmokeContext context)
    {
        RequireContains(context.Xaml, "After add Form", "Property Inspector edit after session activation should be exported.");
    }

    private static void ConfigureDesignerSurfaceStandardControl(MainWindowViewModel vm)
    {
        var button = Control(DesignerControlTypes.Button, "SurfaceButton", 48, 56, 180, 42, text: "Surface button");
        vm.Controls.Add(button);
        vm.SelectSingleControl(button);
    }

    private static void AssertDesignerSurfaceCanBeCreatedWithoutMainWindow(SmokeContext context)
    {
        EnsureAvaloniaRuntimeInitialized();
        var surface = new DesignerSurface();
        if (surface.Session is not null || surface.ViewModel.Session is not null)
            throw new InvalidOperationException("A new DesignerSurface must not require a document or MainWindow during construction.");
        if (surface.Canvas is null || surface.ViewportScrollViewer is null || surface.ZoomPreset is null)
            throw new InvalidOperationException("DesignerSurface did not create its Canvas visual hierarchy.");
    }

    private static void AssertDesignerSurfaceCanAttachDocumentSession(SmokeContext context)
    {
        EnsureAvaloniaRuntimeInitialized();
        var surface = new DesignerSurface
        {
            Context = context.ViewModel,
            Session = context.ViewModel.ActiveSession
        };

        if (!ReferenceEquals(surface.Session, context.ViewModel.ActiveSession)
            || !ReferenceEquals(surface.ViewModel.Session, context.ViewModel.ActiveSession)
            || !ReferenceEquals(surface.ViewModel.Context, context.ViewModel))
        {
            throw new InvalidOperationException("DesignerSurface did not attach the active DesignerDocumentSession through its host-neutral contract.");
        }
    }

    private static void AssertDesignerSurfaceCanSwitchDocumentSessions(SmokeContext context)
    {
        EnsureAvaloniaRuntimeInitialized();
        var vm = context.ViewModel;
        var firstSession = vm.ActiveSession;
        var surface = new DesignerSurface { Context = vm, Session = firstSession };
        var form2 = vm.CreateNewForm();

        surface.Session = vm.ActiveSession;
        if (ReferenceEquals(firstSession, surface.Session)
            || !ReferenceEquals(surface.Session, vm.DocumentSessions[form2.Id]))
        {
            throw new InvalidOperationException("DesignerSurface retained the previous session after the active document changed.");
        }
    }

    private static void AssertDesignerSurfaceRendersStandardControl(SmokeContext context)
    {
        var surface = CreateMainWindowDesignerSurface(context);
        if (!surface.Canvas.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Surface button"))
            throw new InvalidOperationException("MainWindow did not render the standard control through DesignerSurface.Canvas.");
    }

    private static void AssertDesignerSurfaceToolboxUsesSharedRegistry(SmokeContext context)
    {
        EnsureAvaloniaRuntimeInitialized();
        var window = new MainWindow { DataContext = context.ViewModel };
        var toolbox = window.FindControl<DesignerToolbox>("DesignerToolbox")
            ?? throw new InvalidOperationException("MainWindow did not host the extracted DesignerToolbox.");
        if (!ReferenceEquals(toolbox.Context, context.ViewModel))
            throw new InvalidOperationException("DesignerToolbox did not receive the shared registry context.");
        if (context.ViewModel.ToolboxGroups.FirstOrDefault(group => string.Equals(group.ProviderId, "Avalonia", StringComparison.OrdinalIgnoreCase))?.Items.Any() != true
            || context.ViewModel.ToolboxGroups.FirstOrDefault(group => string.Equals(group.ProviderId, "Eremex", StringComparison.OrdinalIgnoreCase))?.Items.Any(item => item.Type == "Eremex.TextEditor") != true)
        {
            throw new InvalidOperationException("DesignerSurface Toolbox must expose the shared Standard and Eremex registry groups.");
        }
    }

    private static void AssertDesignerSurfaceSelectionUpdatesSession(SmokeContext context)
    {
        var vm = context.ViewModel;
        var surface = new DesignerSurface { Context = vm, Session = vm.ActiveSession };
        var button = vm.Controls.Single(control => control.Name == "SurfaceButton");
        vm.SelectSingleControl(button);

        if (!ReferenceEquals(surface.Session?.SelectedControl, button)
            || !ReferenceEquals(vm.SelectedControl, button))
        {
            throw new InvalidOperationException("Canvas selection and DesignerDocumentSession selection diverged.");
        }
    }

    private static void AssertSessionSelectionUpdatesDesignerSurface(SmokeContext context)
    {
        var vm = context.ViewModel;
        var surface = new DesignerSurface { Context = vm, Session = vm.ActiveSession };
        var button = vm.Controls.Single(control => control.Name == "SurfaceButton");
        vm.ActiveSession.SetSelectedControl(button);

        if (!ReferenceEquals(surface.ViewModel.Session?.SelectedControl, button))
            throw new InvalidOperationException("DesignerSurface did not observe the selection stored in its active session.");
    }

    private static void AssertDesignerSurfaceInspectorUsesSessionSelection(SmokeContext context)
    {
        EnsureAvaloniaRuntimeInitialized();
        var vm = context.ViewModel;
        var button = vm.Controls.Single(control => control.Name == "SurfaceButton");
        vm.SelectSingleControl(button);
        var window = new MainWindow { DataContext = vm };
        var inspector = window.FindControl<DesignerPropertyInspector>("DesignerPropertyInspector")
            ?? throw new InvalidOperationException("MainWindow did not host the extracted DesignerPropertyInspector.");

        if (!ReferenceEquals(inspector.Context, vm)
            || !ReferenceEquals(vm.ActiveSession.SelectedControl, button)
            || !vm.PropertyGridCategories.SelectMany(category => category.Rows).Any(row => row.ContextControlId == button.Id))
        {
            throw new InvalidOperationException("DesignerPropertyInspector is not using the selected control from the active document session.");
        }
    }

    private static void AssertDesignerPropertyInspectorFavoritesRoundTrip(SmokeContext context)
    {
        EnsureAvaloniaRuntimeInitialized();
        var vm = context.ViewModel;
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.Controls.Single(control => control.Name == "SurfaceButton");
        vm.SelectSingleControl(button);

        var window = new MainWindow { DataContext = vm };
        var inspector = window.FindControl<DesignerPropertyInspector>("DesignerPropertyInspector")
            ?? throw new InvalidOperationException("MainWindow did not host the extracted DesignerPropertyInspector.");
        var command = inspector.ToggleFavoriteCommand
            ?? throw new InvalidOperationException("DesignerPropertyInspector did not receive the favorite command from its host.");

        if (!ReferenceEquals(command, vm.TogglePropertyGridFavoriteCommand))
            throw new InvalidOperationException("DesignerPropertyInspector favorite command is not bound to the active host command.");

        var inspectorSource = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "DesignerPropertyInspector.axaml"), Encoding.UTF8)
            + File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "DesignerPropertyInspector.axaml.cs"), Encoding.UTF8);
        RequireContains(inspectorSource, "#RootInspector.ToggleFavoriteCommand", "Favorite button must bind through the inspector contract.");
        RequireNotContains(inspectorSource, "RootInspector.DataContext.Context.TogglePropertyGridFavoriteCommand", "Favorite button must not depend on the inherited host DataContext path.");
        RequireNotContains(inspectorSource, "MainWindow", "DesignerPropertyInspector must not depend on MainWindow.");

        inspector.Measure(new Size(420, 800));
        inspector.Arrange(new Rect(0, 0, 420, 800));
        var backgroundRow = GetPropertyGridRow(vm, nameof(DesignControlModel.Background));
        var backgroundStar = inspector.GetVisualDescendants()
            .OfType<Button>()
            .SingleOrDefault(candidate => candidate.Classes.Contains("property-star")
                && ReferenceEquals(candidate.DataContext, backgroundRow));
        if (backgroundStar is null
            || !ReferenceEquals(backgroundStar.Command, command)
            || !ReferenceEquals(backgroundStar.CommandParameter, backgroundRow))
        {
            throw new InvalidOperationException("The Background favorite button did not resolve its host-neutral command and row parameter.");
        }

        backgroundStar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!vm.OutputEntries.Any(entry => entry.Message == "PROPERTY_FAVORITE_CLICK"))
            throw new InvalidOperationException("Favorite button click diagnostics were not written to the workspace log.");

        backgroundStar.Command.Execute(backgroundStar.CommandParameter);
        if (!GetPropertyGridRow(vm, nameof(DesignControlModel.Background)).IsFavorite)
            throw new InvalidOperationException("Background was not added to favorites through its rendered star button.");

        void ToggleAndRequire(string key, bool expected)
        {
            var row = GetPropertyGridRow(vm, key);
            command.Execute(row);
            var refreshed = GetPropertyGridRow(vm, key);
            if (refreshed.IsFavorite != expected)
                throw new InvalidOperationException($"Favorite state for '{key}' should be {expected}, got {refreshed.IsFavorite}.");
        }

        ToggleAndRequire(nameof(DesignControlModel.Background), false);
        ToggleAndRequire(nameof(DesignControlModel.Background), true);
        ToggleAndRequire(nameof(DesignControlModel.Foreground), true);
        ToggleAndRequire(nameof(DesignControlModel.Opacity), true);

        var textBox = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.TextBox, 260, 56, null, false, form1.Id)
            ?? throw new InvalidOperationException("TextBox was not created for the favorite persistence test.");
        vm.SelectSingleControl(textBox);
        vm.SelectSingleControl(button);
        foreach (var key in new[] { nameof(DesignControlModel.Background), nameof(DesignControlModel.Foreground), nameof(DesignControlModel.Opacity) })
        {
            if (!GetPropertyGridRow(vm, key).IsFavorite)
                throw new InvalidOperationException($"Favorite '{key}' was lost after switching the selected control.");
        }

        var form2 = vm.CreateNewForm();
        var form2Button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 48, 56, null, false, form2.Id)
            ?? throw new InvalidOperationException("Form2 Button was not created for the favorite persistence test.");
        vm.SelectSingleControl(form2Button);
        ToggleAndRequire(nameof(DesignControlModel.Margin), true);

        SwitchToForm(vm, form1.Id);
        var restoredButton = vm.Controls.Single(control => control.Id == button.Id);
        vm.ClearSelection();
        vm.SelectSingleControl(restoredButton);
        foreach (var key in new[] { nameof(DesignControlModel.Background), nameof(DesignControlModel.Foreground), nameof(DesignControlModel.Opacity), nameof(DesignControlModel.Margin) })
        {
            if (!GetPropertyGridRow(vm, key).IsFavorite)
                throw new InvalidOperationException($"Favorite '{key}' was lost after form switch or Property Inspector rebuild.");
        }

        SetPropertyGridValue(vm, nameof(DesignControlModel.Width), "211");
        if (Math.Abs(restoredButton.Width - 211) > 0.001)
            throw new InvalidOperationException("An ordinary Property Inspector edit regressed while testing favorites.");

        if (!vm.OutputEntries.Any(entry => entry.Message == "PROPERTY_FAVORITE_TOGGLE")
            || !vm.OutputEntries.Any(entry => entry.Message == "PROPERTY_FAVORITE_REFRESH"))
        {
            throw new InvalidOperationException("Favorite toggle diagnostics were not written to the workspace log.");
        }
    }

    private static void AssertDesignerSurfaceWorksWithFakeHostServices(SmokeContext context)
    {
        EnsureAvaloniaRuntimeInitialized();
        var host = new TestDesignerHostServices();
        var surface = new DesignerSurface
        {
            Context = context.ViewModel,
            Session = context.ViewModel.ActiveSession,
            HostServices = host
        };

        if (!ReferenceEquals(surface.HostServices, host)
            || !ReferenceEquals(surface.ViewModel.HostServices, host)
            || surface.Session is null)
        {
            throw new InvalidOperationException("DesignerSurface did not receive the explicit fake host services contract.");
        }
    }

    private static void AssertDesignerHostServicesRouteClipboardFilePickerDialogAndNotification(SmokeContext context)
    {
        EnsureAvaloniaRuntimeInitialized();
        var registry = new DesignerRegistry();
        BuiltInControlRegistrar.Register(registry);
        registry.RegisterBindingProvider(new ReflectionBindingMetadataProvider());
        var host = new TestDesignerHostServices();
        var vm = new MainWindowViewModel(registry, host);
        var window = new MainWindow(host) { DataContext = vm };

        host.FilePicker.OpenFiles.Enqueue(new TestDesignerHostFile("logo.png", @"C:\host-test\logo.png"));
        var pickedImage = InvokePrivateTask<string?>(window, "PickImagePathAsync");
        if (!string.Equals(pickedImage, @"C:\host-test\logo.png", StringComparison.Ordinal)
            || host.FilePicker.OpenRequests.Count != 1)
        {
            throw new InvalidOperationException("Designer image selection did not use IDesignerFilePickerService.");
        }

        InvokePrivateTask(window, "CopyTextToClipboardAsync", "host clipboard text", "Copied");
        if (!string.Equals(host.Clipboard.LastText, "host clipboard text", StringComparison.Ordinal)
            || host.Notifications.Published.Count != 1)
        {
            throw new InvalidOperationException("Designer clipboard copy did not use IDesignerClipboard and IDesignerNotificationService.");
        }

        vm.Controls.Add(Control(DesignerControlTypes.Button, "HostDialogButton", 20, 20, 120, 36, text: "Host"));
        host.Dialogs.NextResult = DesignerDialogResult.Discard;
        var canDiscard = InvokePrivateTask<bool>(window, "EnsureUnsavedChangesHandledAsync");
        if (!canDiscard || host.Dialogs.Requests.SingleOrDefault()?.Kind != DesignerDialogKind.UnsavedChanges)
            throw new InvalidOperationException("Unsaved-changes confirmation did not use IDesignerDialogService.");

        if (!string.Equals(vm.PluginInstallFolderPath, host.Paths.PluginDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Designer ViewModel did not use IDesignerPathService for the plugin directory.");
    }

    private static void AssertDesignerHostArchitectureGuard(SmokeContext context)
    {
        var root = FindRepositoryRoot();
        var sharedDesignerSource = string.Join(Environment.NewLine, new[]
        {
            File.ReadAllText(Path.Combine(root, "Views", "DesignerSurface.axaml"), Encoding.UTF8),
            File.ReadAllText(Path.Combine(root, "Views", "DesignerSurface.axaml.cs"), Encoding.UTF8),
            File.ReadAllText(Path.Combine(root, "ViewModels", "DesignerSurfaceViewModel.cs"), Encoding.UTF8),
            File.ReadAllText(Path.Combine(root, "DesignerSystem", "DesignerDocumentSession.cs"), Encoding.UTF8)
        });

        foreach (var forbidden in new[] { "MainWindow", "StorageProvider", "TopLevel.GetTopLevel", "AppContext.BaseDirectory", "Process.Start" })
            RequireNotContains(sharedDesignerSource, forbidden, $"Shared Designer layer must not depend directly on '{forbidden}'.");

        RequireContains(File.ReadAllText(Path.Combine(root, "DesignerSystem", "Hosting", "DesignerHostServices.cs"), Encoding.UTF8), "interface IDesignerHostServices", "Host service contract is missing.");
        RequireContains(File.ReadAllText(Path.Combine(root, "Services", "StandaloneDesignerHostServices.cs"), Encoding.UTF8), "StandaloneDesignerHostServices", "Standalone host adapter is missing.");
    }

    private static void AssertMainWindowSwitchFormRebindsSameDesignerSurface(SmokeContext context)
    {
        var vm = context.ViewModel;
        var surface = CreateMainWindowDesignerSurface(context);
        var firstSurface = surface;
        var form2 = vm.CreateNewForm();

        if (!ReferenceEquals(firstSurface, surface)
            || !ReferenceEquals(surface.Session, vm.DocumentSessions[form2.Id]))
        {
            throw new InvalidOperationException("MainWindow did not rebind its existing DesignerSurface to the newly active document session.");
        }
    }

    private static void AssertDesignerSurfaceDoesNotRequireMainWindowConcreteType(SmokeContext context)
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "Views", "DesignerSurface.axaml.cs"), Encoding.UTF8)
            + File.ReadAllText(Path.Combine(root, "Views", "DesignerSurface.axaml"), Encoding.UTF8);
        RequireNotContains(source, "MainWindowViewModel", "DesignerSurface must not require MainWindowViewModel as a concrete dependency.");
        _ = new DesignerSurface { Context = context.ViewModel, Session = context.ViewModel.ActiveSession };
    }

    private static void AssertDesignerSurfaceRendersEremexTextEditor(SmokeContext context)
    {
        var surface = CreateMainWindowDesignerSurface(context);
        var editor = surface.Canvas.GetVisualDescendants()
            .FirstOrDefault(control => string.Equals(control.GetType().FullName, "Eremex.AvaloniaUI.Controls.Editors.TextEditor", StringComparison.Ordinal));
        AssertRealEremexTextEditor(editor as Control ?? throw new InvalidOperationException("DesignerSurface Canvas did not create Eremex TextEditor."), "DesignerSurface Canvas");
    }

    private static void AssertDesignerSurfaceRendersEremexDataGrid(SmokeContext context)
    {
        var surface = CreateMainWindowDesignerSurface(context);
        var grid = surface.Canvas.GetVisualDescendants()
            .FirstOrDefault(control => string.Equals(control.GetType().FullName, "Eremex.AvaloniaUI.Controls.DataGrid.DataGridControl", StringComparison.Ordinal));
        AssertRealEremexDataGrid(grid as Control ?? throw new InvalidOperationException("DesignerSurface Canvas did not create Eremex DataGridControl."), "DesignerSurface Canvas");
    }

    private static DesignerSurface CreateMainWindowDesignerSurface(SmokeContext context)
    {
        EnsureAvaloniaRuntimeInitialized();
        var window = new MainWindow { DataContext = context.ViewModel };
        var surface = window.FindControl<DesignerSurface>("DesignerSurface")
            ?? throw new InvalidOperationException("MainWindow did not host a DesignerSurface named 'DesignerSurface'.");
        surface.Context = context.ViewModel;
        surface.Session = context.ViewModel.ActiveSession;
        return surface;
    }

    private static void AssertAxamlImportCanvasButtonTextBox(SmokeContext context)
    {
        var result = ImportRoundTripFixture();
        var button = result.Document.Controls.Single(control => control.Name == "Button1");
        var textBox = result.Document.Controls.Single(control => control.Name == "TextBox1");
        if (result.Document.Controls.Count != 2
            || button.Type != DesignerControlTypes.Button
            || textBox.Type != DesignerControlTypes.TextBox
            || button.Text != "Кнопка"
            || Math.Abs(button.X - 100) > 0.001
            || Math.Abs(textBox.Y - 260) > 0.001
            || result.Document.Controls[0].Name != "TextBox1")
        {
            throw new InvalidOperationException("AXAML import did not project the supported Canvas/Button/TextBox subset or Canvas.ZIndex order.");
        }

        var vm = CreateViewModel("AxamlImportCanvasButtonTextBox");
        vm.LoadAxamlImportedDocument(result, "MainWindow.axaml");
        var surface = CreateMainWindowDesignerSurface(new SmokeContext(context.Scenario, vm, context.ProjectPath, context.Xaml, context.CSharp, context.GeneratedFiles, context.ChecklistText, context.DiagnosticsText));
        if (!surface.Canvas.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Кнопка")
            || !vm.IsAxamlRoundTripDocument)
        {
            throw new InvalidOperationException("Imported AXAML controls were not rendered through the existing DesignerSurface.");
        }

        vm.Controls.Single(control => control.Name == "Button1").Text = "Сохранить из Designer";
        var viewModelPatch = vm.CreateActiveAxamlPatch(result.RoundTripDocument.OriginalText);
        RequireContains(viewModelPatch.PatchedText, "Content=\"Сохранить из Designer\"", "ViewModel did not route imported AXAML save through AxamlPatchWriter.");
        vm.MarkAxamlRoundTripSaved("MainWindow.axaml", viewModelPatch.PatchedText);
        var repeatPatch = vm.CreateActiveAxamlPatch(viewModelPatch.PatchedText);
        if (!repeatPatch.CanApply || repeatPatch.HasChanges)
            throw new InvalidOperationException("AXAML source map was not refreshed after an applied patch.");
    }

    private static void AssertAxamlRoundTripUpdatesButtonProperties(SmokeContext context)
    {
        var result = ImportRoundTripFixture();
        var button = result.Document.Controls.Single(control => control.Name == "Button1");
        button.Text = "Сохранить";
        button.Width = 220;
        button.X = 180;
        button.Y = 230;
        result.Document.Controls.Single(control => control.Name == "TextBox1").Text = "World";

        var patch = new AxamlPatchWriter().CreatePatch(result.RoundTripDocument, result.Document, result.RoundTripDocument.OriginalText);
        if (!patch.CanApply || patch.Edits.Count != 5)
            throw new InvalidOperationException("AXAML patch writer did not create the expected minimal attribute edits.");
        RequireContains(patch.PatchedText, "Content=\"Сохранить\"", "Button Content was not patched.");
        RequireContains(patch.PatchedText, "Width=\"220\"", "Button Width was not patched.");
        RequireContains(patch.PatchedText, "Canvas.Left=\"180\"", "Button Canvas.Left was not patched.");
        RequireContains(patch.PatchedText, "Canvas.Top=\"230\"", "Button Canvas.Top was not patched.");
        RequireContains(patch.PatchedText, "Text=\"World\"", "TextBox.Text was not patched.");
    }

    private static void AssertAxamlRoundTripPreservesComment(SmokeContext context)
    {
        var result = ImportRoundTripFixture();
        result.Document.Controls.Single(control => control.Name == "Button1").Width = 200;
        var patch = new AxamlPatchWriter().CreatePatch(result.RoundTripDocument, result.Document, result.RoundTripDocument.OriginalText);
        RequireContains(patch.PatchedText, "<!-- пользовательский комментарий: не удалять -->", "Round-trip removed a user comment.");
    }

    private static void AssertAxamlRoundTripPreservesUnknownAttribute(SmokeContext context)
    {
        var result = ImportRoundTripFixture();
        result.Document.Controls.Single(control => control.Name == "Button1").Width = 200;
        var patch = new AxamlPatchWriter().CreatePatch(result.RoundTripDocument, result.Document, result.RoundTripDocument.OriginalText);
        RequireContains(patch.PatchedText, "Custom.Unknown=\"keep-me\"", "Round-trip removed an unknown attribute.");
        if (!result.Diagnostics.Any(diagnostic => diagnostic.Code == "AXAML_IMPORT_UNKNOWN_ATTRIBUTE_PRESERVED"))
            throw new InvalidOperationException("Unknown attribute preservation was not diagnosed.");
    }

    private static void AssertAxamlRoundTripPreservesUnknownControl(SmokeContext context)
    {
        var result = ImportRoundTripFixture();
        result.Document.Controls.Single(control => control.Name == "Button1").Width = 200;
        var patch = new AxamlPatchWriter().CreatePatch(result.RoundTripDocument, result.Document, result.RoundTripDocument.OriginalText);
        RequireContains(patch.PatchedText, "<custom:HandWrittenControl Custom.Property=\"keep-me-too\" />", "Round-trip removed an unsupported control.");
        if (result.CapabilityReport.Level != AxamlCapabilityLevel.PartiallyEditable)
            throw new InvalidOperationException("Unsupported control must put the document into partial designer mode.");
    }

    private static void AssertAxamlRoundTripPreservesRawContent(SmokeContext context)
    {
        const string source = "<Window xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><Canvas><Button x:Name=\"ManualButton\">Ручной контент</Button><Button x:Name=\"Button1\" Content=\"Кнопка\" Width=\"160\" Height=\"40\" Canvas.Left=\"100\" Canvas.Top=\"200\" /></Canvas></Window>";
        var result = new AxamlImportService().Import(source, "RawContent.axaml");
        if (result.Document.Controls.Count != 1 || result.Document.Controls.Single().Name != "Button1")
            throw new InvalidOperationException("Raw content control must remain outside the Phase 1 editable projection.");

        result.Document.Controls.Single().Width = 200;
        var patch = new AxamlPatchWriter().CreatePatch(result.RoundTripDocument, result.Document, source);
        RequireContains(patch.PatchedText, "<Button x:Name=\"ManualButton\">Ручной контент</Button>", "Round-trip changed raw inner content syntax.");
    }

    private static void AssertAxamlRoundTripCanInsertNewButtonIntoCanvas(SmokeContext context)
    {
        var result = ImportRoundTripFixture();
        result.Document.Controls.Add(new DesignerControlFileModel
        {
            Id = "button2",
            Type = DesignerControlTypes.Button,
            Name = "Button2",
            Text = "Новая кнопка",
            Width = 160,
            Height = 40,
            X = 300,
            Y = 200
        });

        var patch = new AxamlPatchWriter().CreatePatch(result.RoundTripDocument, result.Document, result.RoundTripDocument.OriginalText);
        RequireContains(patch.PatchedText, "x:Name=\"Button2\"", "New Button fragment was not inserted into Canvas.");
        if (patch.PatchedText.IndexOf("x:Name=\"Button2\"", StringComparison.Ordinal) > patch.PatchedText.IndexOf("</Canvas>", StringComparison.Ordinal))
            throw new InvalidOperationException("New Button was inserted outside Canvas.");
    }

    private static void AssertAxamlRoundTripDeletesOnlyOwnedElement(SmokeContext context)
    {
        var result = ImportRoundTripFixture();
        result.Document.Controls.RemoveAll(control => control.Name == "Button1");
        var patch = new AxamlPatchWriter().CreatePatch(result.RoundTripDocument, result.Document, result.RoundTripDocument.OriginalText);
        RequireNotContains(patch.PatchedText, "x:Name=\"Button1\"", "Imported Button was not deleted.");
        RequireContains(patch.PatchedText, "<!-- пользовательский комментарий: не удалять -->", "Delete removed an adjacent user comment.");
        RequireContains(patch.PatchedText, "x:Name=\"TextBox1\"", "Delete changed an unrelated imported control.");
    }

    private static void AssertAxamlRoundTripDoesNotReformatWholeDocument(SmokeContext context)
    {
        var result = ImportRoundTripFixture();
        result.Document.Controls.Single(control => control.Name == "Button1").Width = 200;
        var patch = new AxamlPatchWriter().CreatePatch(result.RoundTripDocument, result.Document, result.RoundTripDocument.OriginalText);
        if (patch.Edits.Count != 1 || patch.Edits.Single().Length != "160".Length || patch.Edits.Single().NewText != "200")
            throw new InvalidOperationException("Changing one property must produce one value-only text edit.");
        RequireContains(patch.PatchedText, "        <TextBox" + result.RoundTripDocument.Syntax.NewLine, "Unchanged TextBox formatting was modified.");
    }

    private static void AssertAxamlRoundTripDetectsExternalChange(SmokeContext context)
    {
        var result = ImportRoundTripFixture();
        result.Document.Controls.Single(control => control.Name == "Button1").Width = 200;
        var patch = new AxamlPatchWriter().CreatePatch(result.RoundTripDocument, result.Document, result.RoundTripDocument.OriginalText + "\n<!-- external -->");
        if (patch.CanApply || !patch.ExternalChangeDetected || !patch.Diagnostics.Any(diagnostic => diagnostic.Code == "AXAML_EXTERNAL_CHANGE_DETECTED"))
            throw new InvalidOperationException("External AXAML modification was not detected before save.");
    }

    private static AxamlImportResult ImportRoundTripFixture()
    {
        var fixturePath = Path.Combine(FindRepositoryRoot(), "Samples", "RoundTrip", "SimpleCanvas", "MainWindow.axaml");
        var source = File.ReadAllText(fixturePath, Encoding.UTF8);
        return new AxamlImportService().Import(source, fixturePath);
    }

    private static void AssertVsHostProtocolHandshakeWorks(SmokeContext context)
    {
        var pipeName = $"{DesignerHostProtocol.PipePrefix}.smoke.{Guid.NewGuid():N}";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = Task.Run(async () =>
        {
            using var server = NamedPipeProtocolConnection.CreateServer(pipeName);
            await server.WaitForConnectionAsync(cancellation.Token);
            using var connection = new NamedPipeProtocolConnection(server);
            var hello = await connection.ReceiveAsync(cancellation.Token)
                ?? throw new InvalidOperationException("Protocol client closed before Hello.");
            if (hello.MessageType != DesignerHostMessageTypes.Hello)
                throw new InvalidOperationException("Protocol client did not send Hello.");

            await connection.SendAsync(DesignerHostMessageTypes.HelloAck, hello.RequestId, hello.DocumentId, new HelloAckPayload
            {
                HostName = "SmokeHost",
                HostVersion = "0.1",
                ProtocolVersion = DesignerHostProtocol.CurrentVersion
            }, cancellation.Token);
        }, cancellation.Token);

        using var client = NamedPipeProtocolConnection.CreateClient(pipeName);
        client.ConnectAsync(cancellation.Token).GetAwaiter().GetResult();
        using var connection = new NamedPipeProtocolConnection(client);
        connection.SendAsync(DesignerHostMessageTypes.Hello, "handshake", string.Empty, new HelloPayload
        {
            ClientName = "SmokeClient",
            ClientVersion = "0.1"
        }, cancellation.Token).GetAwaiter().GetResult();
        var acknowledgement = connection.ReceiveAsync(cancellation.Token).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("Protocol server did not return HelloAck.");
        if (acknowledgement.MessageType != DesignerHostMessageTypes.HelloAck
            || connection.GetPayload<HelloAckPayload>(acknowledgement)?.ProtocolVersion != DesignerHostProtocol.CurrentVersion)
        {
            throw new InvalidOperationException("Protocol handshake acknowledgement is invalid.");
        }

        serverTask.GetAwaiter().GetResult();
    }

    private static void AssertVsHostCanStartAndAcceptConnection(SmokeContext context)
    {
        var executable = Path.Combine(FindRepositoryRoot(), "AvaloniaDesigner.VsHost", "bin", "Debug", "net6.0", "AvaloniaDesigner.VsHost.exe");
        RequireFileExists(executable, "VsHost executable was not built before the smoke test.");

        var pipeName = $"{DesignerHostProtocol.PipePrefix}.smoke.{Guid.NewGuid():N}";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = $"--pipe \"{pipeName}\"",
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("VsHost process did not start.");

        try
        {
            using var client = NamedPipeProtocolConnection.CreateClient(pipeName);
            client.ConnectAsync(cancellation.Token).GetAwaiter().GetResult();
            using var connection = new NamedPipeProtocolConnection(client);

            connection.SendAsync(DesignerHostMessageTypes.Hello, "hello", string.Empty, new HelloPayload
            {
                ClientName = "FormDesigner.ExportSmokeTests",
                ClientVersion = "0.1"
            }, cancellation.Token).GetAwaiter().GetResult();
            var hello = connection.ReceiveAsync(cancellation.Token).GetAwaiter().GetResult()
                ?? throw new InvalidOperationException("VsHost did not answer Hello.");
            if (hello.MessageType != DesignerHostMessageTypes.HelloAck)
                throw new InvalidOperationException($"VsHost returned '{hello.MessageType}' instead of HelloAck.");

            var source = ImportRoundTripFixture().RoundTripDocument.OriginalText;
            var open = CreateVsHostOpenDocumentPayload(source, version: 1);
            connection.SendAsync(DesignerHostMessageTypes.OpenDocument, "open", "smoke-document", open, cancellation.Token).GetAwaiter().GetResult();
            var opened = connection.ReceiveAsync(cancellation.Token).GetAwaiter().GetResult()
                ?? throw new InvalidOperationException("VsHost did not return DocumentOpened.");
            var openedPayload = connection.GetPayload<DocumentOpenedPayload>(opened);
            if (opened.MessageType != DesignerHostMessageTypes.DocumentOpened || openedPayload is null || !openedPayload.CanEdit)
                throw new InvalidOperationException("VsHost did not import the supported AXAML snapshot into the shared Designer surface.");

            connection.SendAsync(DesignerHostMessageTypes.HostShutdown, "shutdown", "smoke-document", new EmptyPayload(), cancellation.Token).GetAwaiter().GetResult();
            if (!process.WaitForExit(10_000))
                throw new InvalidOperationException("VsHost did not stop after HostShutdown.");
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    private static void AssertVsHostUsesSharedDesignerSurface(SmokeContext context)
    {
        if (!typeof(MainWindow).IsAssignableFrom(typeof(VsHostWindow))
            || typeof(MainWindow).Assembly != typeof(DesignerSurface).Assembly)
        {
            throw new InvalidOperationException("VsHost must host the existing MainWindow/DesignerSurface assembly rather than a copied Designer UI.");
        }

        var hostSource = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "AvaloniaDesigner.VsHost", "VsHostWindow.cs"), Encoding.UTF8);
        RequireNotContains(hostSource, "VsDesignerSurface", "VsHost must not define a second DesignerSurface.");
        RequireContains(hostSource, "class VsHostWindow : MainWindow", "VsHost must reuse the existing Designer interaction host for this PoC.");
    }

    private static void AssertVsixBridgeDoesNotReferenceAvaloniaVisualAssemblies(SmokeContext context)
    {
        var root = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(root, "AvaloniaDesigner.VSIX", "AvaloniaDesigner.VSIX.csproj"), Encoding.UTF8);
        RequireContains(project, "AvaloniaDesigner.Host.Protocol", "VSIX bridge must reference the host-neutral Protocol project.");
        RequireNotContains(project, "<ProjectReference Include=\"..\\FormDesigner.csproj\"", "VSIX bridge must not reference FormDesigner.");
        RequireNotContains(project, "<ProjectReference Include=\"..\\AvaloniaDesigner.VsHost", "VSIX bridge must package VsHost as content, not reference it.");
        RequireNotContains(project, "<PackageReference Include=\"Eremex", "VSIX bridge must not reference Eremex.");
        RequireNotContains(project, "<ProjectReference Include=\"..\\Plugins\\Eremex", "VSIX bridge must not reference Eremex.");

        var source = string.Join(Environment.NewLine, Directory.GetFiles(Path.Combine(root, "AvaloniaDesigner.VSIX"), "*.cs", SearchOption.AllDirectories)
            .Select(path => File.ReadAllText(path, Encoding.UTF8)));
        RequireNotContains(source, "using Avalonia;", "VSIX bridge source must not load Avalonia visual APIs.");
        RequireNotContains(source, "using Eremex", "VSIX bridge source must not load Eremex APIs.");
        RequireNotContains(source, "using FormDesigner", "VSIX bridge source must not load FormDesigner APIs.");

        var archivePath = Path.Combine(root, "AvaloniaDesigner.VSIX", "bin", "Debug", "net472", "AvaloniaDesigner.VSIX.vsix");
        RequireFileExists(archivePath, "VSIX package must be built before its dependency boundary is verified.");
        using var archive = ZipFile.OpenRead(archivePath);
        var forbiddenRootPayload = archive.Entries.Any(entry =>
            !entry.FullName.StartsWith("VsHost/", StringComparison.OrdinalIgnoreCase)
            && (entry.FullName.StartsWith("Avalonia.", StringComparison.OrdinalIgnoreCase)
                || entry.FullName.StartsWith("Eremex.", StringComparison.OrdinalIgnoreCase)
                || entry.FullName.StartsWith("FormDesigner.", StringComparison.OrdinalIgnoreCase)));
        if (forbiddenRootPayload)
            throw new InvalidOperationException("VSIX root contains a visual Designer assembly instead of only bridge/protocol assets.");
    }

    private static void AssertVsixCommandTableRegistersVisibleToolsCommand(SmokeContext context)
    {
        var root = FindRepositoryRoot();
        var vsct = File.ReadAllText(Path.Combine(root, "AvaloniaDesigner.VSIX", "AvaloniaDesigner.vsct"), Encoding.UTF8);
        RequireContains(vsct, "id=\"DesignerMenu\"", "VSIX command table must declare the Avalonia Designer Tools submenu.");
        RequireContains(vsct, "id=\"IDG_VS_MM_TOOLSADDINS\"", "VSIX command table must place the submenu in the standard Tools/Add-ins group.");
        RequireContains(vsct, "<CommandFlag>AlwaysCreate</CommandFlag>", "VSIX submenu must be created before its package is loaded.");
        RequireContains(vsct, "<ButtonText>Avalonia UI Visual Designer</ButtonText>", "VSIX Tools submenu text is missing.");
        RequireContains(vsct, "<ButtonText>Open Designer</ButtonText>", "VSIX Open Designer command text is missing.");

        var packageSource = File.ReadAllText(Path.Combine(root, "AvaloniaDesigner.VSIX", "AvaloniaDesignerVsixPackage.cs"), Encoding.UTF8);
        RequireContains(packageSource, "ProvideMenuResource(\"Menus.ctmenu\", 2)", "VSIX package must register the current command table resource.");
        RequireContains(packageSource, "ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string", "VSIX package must request a deterministic no-solution background load.");
        RequireContains(packageSource, "ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExistsAndFullyLoaded_string", "VSIX package must request a deterministic completed-solution load.");

        var commandSource = File.ReadAllText(Path.Combine(root, "AvaloniaDesigner.VSIX", "OpenInAvaloniaDesignerCommand.cs"), Encoding.UTF8);
        RequireContains(commandSource, "command.Visible = true;", "VSIX command must remain visible as a discoverable Tools entry point.");
        RequireContains(commandSource, "command.Enabled = true;", "VSIX command must defer AXAML validation to execution instead of disappearing from Tools.");

        var archivePath = Path.Combine(root, "AvaloniaDesigner.VSIX", "bin", "Debug", "net472", "AvaloniaDesigner.VSIX.vsix");
        RequireFileExists(archivePath, "VSIX package must be built before its command registration is verified.");
        using var archive = ZipFile.OpenRead(archivePath);
        var pkgdef = archive.GetEntry("AvaloniaDesigner.VSIX.pkgdef")
            ?? throw new InvalidOperationException("VSIX package does not contain its PkgDef registration.");
        using var reader = new StreamReader(pkgdef.Open(), Encoding.UTF8);
        var pkgdefText = reader.ReadToEnd();
        RequireContains(pkgdefText, "[$RootKey$\\Menus]", "VSIX PkgDef is missing the Visual Studio Menus registration.");
        RequireContains(pkgdefText, "Menus.ctmenu, 2", "VSIX PkgDef does not point Visual Studio at the current compiled command table.");
    }

    private static void AssertVsixPackageRuntimeDependencyClosureIsValid(SmokeContext context)
    {
        var root = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(root, "AvaloniaDesigner.VSIX", "AvaloniaDesigner.VSIX.csproj"), Encoding.UTF8);
        RequireContains(project, "<TargetFramework>net472</TargetFramework>", "VSIX bridge must remain compatible with the Visual Studio .NET Framework process.");
        RequireContains(project, "<UseCodebase>true</UseCodebase>", "VSIX package assembly must receive a $PackageFolder$ CodeBase registration.");

        var packageSource = File.ReadAllText(Path.Combine(root, "AvaloniaDesigner.VSIX", "AvaloniaDesignerVsixPackage.cs"), Encoding.UTF8);
        RequireContains(packageSource, "[ProvideBindingPath]", "VSIX package must expose its private bridge dependency folder to the Visual Studio loader.");
        RequireContains(packageSource, "AVALONIA_DESIGNER_VSIX_INITIALIZE_FAILED", "VSIX package must log package initialization failures to ActivityLog.");
        RequireContains(packageSource, "VsixPackageLoadProbe.Write", "VSIX package must write a service-independent load probe for PoC diagnostics.");

        var commandSource = File.ReadAllText(Path.Combine(root, "AvaloniaDesigner.VSIX", "OpenInAvaloniaDesignerCommand.cs"), Encoding.UTF8);
        var initializationStart = commandSource.IndexOf("public static async Task InitializeAsync", StringComparison.Ordinal);
        var executeStart = commandSource.IndexOf("private async Task ExecuteAsync", StringComparison.Ordinal);
        var dteLookup = commandSource.IndexOf("GetServiceAsync(typeof(DTE))", StringComparison.Ordinal);
        if (initializationStart < 0 || executeStart < 0 || dteLookup < executeStart)
            throw new InvalidOperationException("VSIX must defer DTE lookup until command execution so package initialization can always register the menu command.");

        var archivePath = Path.Combine(root, "AvaloniaDesigner.VSIX", "bin", "Debug", "net472", "AvaloniaDesigner.VSIX.vsix");
        RequireFileExists(archivePath, "VSIX package must be built before validating its runtime dependency closure.");
        using var archive = ZipFile.OpenRead(archivePath);
        var rootEntries = archive.Entries
            .Where(entry => !entry.FullName.Contains('/') && entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.FullName)
            .ToArray();

        if (!rootEntries.Contains("AvaloniaDesigner.VSIX.dll", StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("VSIX package assembly is missing from the VSIX root.");
        if (!rootEntries.Contains("AvaloniaDesigner.Host.Protocol.dll", StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("VSIX bridge dependency AvaloniaDesigner.Host.Protocol.dll is missing from the VSIX root.");
        if (rootEntries.Any(entry => entry.StartsWith("Avalonia.", StringComparison.OrdinalIgnoreCase)
            || entry.StartsWith("Eremex.", StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry, "FormDesigner.dll", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("VSIX root contains an Avalonia/Eremex/FormDesigner visual dependency that must remain inside VsHost.");
        }

        var pkgdef = archive.GetEntry("AvaloniaDesigner.VSIX.pkgdef")
            ?? throw new InvalidOperationException("VSIX package does not contain its PkgDef registration.");
        using var reader = new StreamReader(pkgdef.Open(), Encoding.UTF8);
        var pkgdefText = reader.ReadToEnd();
        RequireContains(pkgdefText, "\"CodeBase\"=\"$PackageFolder$\\AvaloniaDesigner.VSIX.dll\"", "VSIX PkgDef cannot locate its own package assembly after installation.");
        RequireContains(pkgdefText, "[$RootKey$\\BindingPaths\\", "VSIX PkgDef must register the extension folder for AvaloniaDesigner.Host.Protocol.dll.");
        RequireContains(pkgdefText, "=dword:00000002", "VSIX PkgDef must request background package loading.");
    }

    private static void AssertVsHostReturnsMinimalAxamlPatch(SmokeContext context)
    {
        var result = ImportRoundTripFixture();
        var viewModel = CreateViewModel("VsHostReturnsMinimalAxamlPatch");
        viewModel.LoadAxamlImportedDocument(result, "MainWindow.axaml");
        var button = viewModel.Controls.Single(control => control.Name == "Button1");
        button.Text = "New";
        button.Width = 220;
        button.X = 180;

        var patch = viewModel.CreateActiveAxamlPatch(result.RoundTripDocument.OriginalText);
        if (!patch.CanApply || patch.Edits.Count != 3)
            throw new InvalidOperationException("VsHost must return exactly the three changed attribute values as a minimal patch.");
        if (patch.Edits.Any(edit => edit.Length > "Content".Length + "Button1".Length))
            throw new InvalidOperationException("VsHost patch replaced more source text than an attribute value.");

        RequireContains(patch.PatchedText, "Content=\"New\"", "VsHost patch did not update Button.Content.");
        RequireContains(patch.PatchedText, "Width=\"220\"", "VsHost patch did not update Button.Width.");
        RequireContains(patch.PatchedText, "Canvas.Left=\"180\"", "VsHost patch did not update Button Canvas.Left.");
    }

    private static void AssertVsHostRoundTripPreservesComment(SmokeContext context)
    {
        var result = ImportRoundTripFixture();
        result.Document.Controls.Single(control => control.Name == "Button1").Text = "New";
        var patch = new AxamlPatchWriter().CreatePatch(result.RoundTripDocument, result.Document, result.RoundTripDocument.OriginalText);
        RequireContains(patch.PatchedText, "<!-- пользовательский комментарий: не удалять -->", "VsHost round-trip removed a comment.");
    }

    private static void AssertVsHostRoundTripPreservesUnknownAttribute(SmokeContext context)
    {
        var result = ImportRoundTripFixture();
        result.Document.Controls.Single(control => control.Name == "TextBox1").Text = "World";
        var patch = new AxamlPatchWriter().CreatePatch(result.RoundTripDocument, result.Document, result.RoundTripDocument.OriginalText);
        RequireContains(patch.PatchedText, "Custom.Unknown=\"keep-me\"", "VsHost round-trip removed an unknown attribute.");
    }

    private static void AssertVsHostRejectsPatchForStaleDocumentVersion(SmokeContext context)
    {
        const string source = "<Window><Canvas /></Window>";
        var opened = CreateVsHostOpenDocumentPayload(source, version: 7);
        var stalePatch = new ApplyDesignerPatchPayload
        {
            ExpectedVersion = 6,
            ExpectedChecksum = DesignerHostProtocol.ComputeChecksum("different"),
            Edits = new List<TextEditPayload>()
        };
        if (DesignerHostPatchGuard.Matches(opened, stalePatch))
            throw new InvalidOperationException("VS bridge accepted a patch created for a stale AXAML snapshot.");
    }

    private static void AssertBridgeSurvivesVsHostDisconnect(SmokeContext context)
    {
        var pipeName = $"{DesignerHostProtocol.PipePrefix}.disconnect.{Guid.NewGuid():N}";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var server = NamedPipeProtocolConnection.CreateServer(pipeName);
        var connectTask = server.WaitForConnectionAsync(cancellation.Token);
        using (var client = NamedPipeProtocolConnection.CreateClient(pipeName))
        {
            client.ConnectAsync(cancellation.Token).GetAwaiter().GetResult();
            connectTask.GetAwaiter().GetResult();
        }

        using var bridgeConnection = new NamedPipeProtocolConnection(server);
        var disconnected = bridgeConnection.ReceiveAsync(cancellation.Token).GetAwaiter().GetResult();
        if (disconnected is not null)
            throw new InvalidOperationException("Bridge did not observe a clean VsHost disconnect.");
    }

    private static OpenDocumentPayload CreateVsHostOpenDocumentPayload(string source, long version) => new()
    {
        FilePath = "MainWindow.axaml",
        Text = source,
        Version = version,
        Checksum = DesignerHostProtocol.ComputeChecksum(source),
        ProjectPath = "SimpleAvaloniaApp.csproj",
        ProjectName = "SimpleAvaloniaApp",
        TargetFramework = "net6.0",
        AvaloniaVersion = AvaloniaVersion
    };

    private static void ConfigureMultiFormToolboxDropPropertyEdit(MainWindowViewModel vm)
    {
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");

        var form1Button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created through toolbox drop path.");
        var form1Border = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Border, 40, 100, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Border was not created through toolbox drop path.");
        var form1Grid = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.DataGrid, 240, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 DataGrid was not created through toolbox drop path.");

        form1Button.Name = "Form1Button";
        form1Border.Name = "Form1Border";
        form1Grid.Name = "Form1Grid";

        vm.SelectSingleControl(form1Button);
        SetPropertyGridValue(vm, nameof(DesignControlModel.Text), "Form1 inspector text");
        SetPropertyGridValue(vm, nameof(DesignControlModel.Width), "188");
        RequirePropertyGridContext(vm, form1.Id, form1Button.Id);

        vm.NewFormEditorCommand?.Execute(null);
        var form2 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form2 missing.");
        form2.Name = "Form2";
        form2.Document.FormTitle = "Form2";
        vm.FormTitle = "Form2";

        var form2Button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 60, 54, null, false, form2.Id)
            ?? throw new InvalidOperationException("Form2 Button was not created through toolbox drop path.");
        var form2Border = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Border, 60, 116, null, false, form2.Id)
            ?? throw new InvalidOperationException("Form2 Border was not created through toolbox drop path.");
        var form2Grid = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.DataGrid, 270, 54, null, false, form2.Id)
            ?? throw new InvalidOperationException("Form2 DataGrid was not created through toolbox drop path.");

        form2Button.Name = "Form2Button";
        form2Border.Name = "Form2Border";
        form2Grid.Name = "Form2Grid";

        var rejected = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.TextBox, 10, 10, null, false, form1.Id);
        if (rejected is not null)
            throw new InvalidOperationException("Toolbox drop should be rejected when drag source document differs from active form.");

        vm.SelectSingleControl(form2Button);
        SetPropertyGridValue(vm, nameof(DesignControlModel.Text), "Form2 inspector text");
        SetPropertyGridValue(vm, nameof(DesignControlModel.Width), "204");
        RequirePropertyGridContext(vm, form2.Id, form2Button.Id);

        SwitchToForm(vm, form1.Id);
        RequireActiveControls(vm, "Form1Button", "Form1Border", "Form1Grid");
        RequireNoActiveControls(vm, "Form2Button", "Form2Border", "Form2Grid");
        var restoredForm1Button = vm.Controls.Single(control => control.Name == "Form1Button");
        if (restoredForm1Button.Text != "Form1 inspector text" || Math.Abs(restoredForm1Button.Width - 188) > 0.001)
            throw new InvalidOperationException("Form1 PropertyGrid edits were not preserved after creating/editing Form2.");

        SwitchToForm(vm, form2.Id);
        RequireActiveControls(vm, "Form2Button", "Form2Border", "Form2Grid");
        RequireNoActiveControls(vm, "Form1Button", "Form1Border", "Form1Grid");
        var restoredForm2Button = vm.Controls.Single(control => control.Name == "Form2Button");
        if (restoredForm2Button.Text != "Form2 inspector text" || Math.Abs(restoredForm2Button.Width - 204) > 0.001)
            throw new InvalidOperationException("Form2 PropertyGrid edits were not preserved after switching documents.");

        var form1Ids = form1.Document.Controls.Select(control => control.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var form2Ids = form2.Document.Controls.Select(control => control.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (form1Ids.Overlaps(form2Ids))
            throw new InvalidOperationException("Form documents share control ids after toolbox drop workflow.");

        SwitchToForm(vm, form1.Id);
    }

    private static void AssertMultiFormToolboxDropPropertyEdit(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml");
        RequireGeneratedFile(context, "MainWindow.axaml.cs");
        RequireGeneratedFile(context, "Form2.axaml");
        RequireGeneratedFile(context, "Form2.axaml.cs");
        RequireContains(context.Xaml, "Form1 inspector text", "Form1 edited Button text should be exported.");
        RequireNotContains(context.Xaml, "Form2 inspector text", "Active Form1 export should not contain Form2 Button text.");
        var form2File = context.GeneratedFiles.First(file => string.Equals(file.Path, "Form2.axaml", StringComparison.OrdinalIgnoreCase));
        RequireContains(form2File.Content, "Form2 inspector text", "Form2 edited Button text should be exported.");
        RequireNotContains(form2File.Content, "Form1 inspector text", "Form2 export should not contain Form1 Button text.");
    }

    private static void ConfigureMultiFormSameControlNamesPropertyGridEdit(MainWindowViewModel vm)
    {
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");

        var form1Button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 24, 36, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button1 was not created.");
        var form1Border = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Border, 24, 96, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Border1 was not created.");
        var form1Grid = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.DataGrid, 240, 36, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 DataGrid1 was not created.");

        RequireSameDefaultNames(form1Button, form1Border, form1Grid);

        vm.SelectSingleControl(form1Button);
        SetPropertyGridValue(vm, nameof(DesignControlModel.Text), "Button Form1");
        RequirePropertyGridContext(vm, form1.Id, form1Button.Id);

        vm.SelectSingleControl(form1Border);
        SetPropertyGridValue(vm, nameof(DesignControlModel.Width), "300");
        RequirePropertyGridContext(vm, form1.Id, form1Border.Id);

        vm.SelectSingleControl(form1Grid);
        SetPropertyGridValue(vm, nameof(DesignControlModel.Width), "400");
        RequirePropertyGridContext(vm, form1.Id, form1Grid.Id);

        vm.NewFormEditorCommand?.Execute(null);
        var form2 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form2 missing.");
        form2.Name = "Form2";
        form2.Document.FormTitle = "Form2";
        vm.FormTitle = "Form2";

        var form2Button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 24, 36, null, false, form2.Id)
            ?? throw new InvalidOperationException("Form2 Button1 was not created.");
        var form2Border = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Border, 24, 96, null, false, form2.Id)
            ?? throw new InvalidOperationException("Form2 Border1 was not created.");
        var form2Grid = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.DataGrid, 240, 36, null, false, form2.Id)
            ?? throw new InvalidOperationException("Form2 DataGrid1 was not created.");

        RequireSameDefaultNames(form2Button, form2Border, form2Grid);

        vm.SelectSingleControl(form2Button);
        SetPropertyGridValue(vm, nameof(DesignControlModel.Text), "Button Form2");
        RequirePropertyGridContext(vm, form2.Id, form2Button.Id);

        vm.SelectSingleControl(form2Border);
        SetPropertyGridValue(vm, nameof(DesignControlModel.Width), "500");
        RequirePropertyGridContext(vm, form2.Id, form2Border.Id);

        vm.SelectSingleControl(form2Grid);
        SetPropertyGridValue(vm, nameof(DesignControlModel.Width), "600");
        RequirePropertyGridContext(vm, form2.Id, form2Grid.Id);

        SwitchToForm(vm, form1.Id);
        RequireActiveControls(vm, "Button1", "Border1", "DataGrid1");
        var restoredForm1Button = vm.Controls.Single(control => control.Name == "Button1");
        var restoredForm1Border = vm.Controls.Single(control => control.Name == "Border1");
        var restoredForm1Grid = vm.Controls.Single(control => control.Name == "DataGrid1");
        if (restoredForm1Button.Text != "Button Form1")
            throw new InvalidOperationException("Form1.Button1 text was overwritten by Form2.Button1.");
        if (Math.Abs(restoredForm1Border.Width - 300) > 0.001)
            throw new InvalidOperationException("Form1.Border1 width was overwritten by Form2.Border1.");
        if (Math.Abs(restoredForm1Grid.Width - 400) > 0.001)
            throw new InvalidOperationException("Form1.DataGrid1 width was overwritten by Form2.DataGrid1.");

        SwitchToForm(vm, form2.Id);
        RequireActiveControls(vm, "Button1", "Border1", "DataGrid1");
        var restoredForm2Button = vm.Controls.Single(control => control.Name == "Button1");
        var restoredForm2Border = vm.Controls.Single(control => control.Name == "Border1");
        var restoredForm2Grid = vm.Controls.Single(control => control.Name == "DataGrid1");
        if (restoredForm2Button.Text != "Button Form2")
            throw new InvalidOperationException("Form2.Button1 text was overwritten by Form1.Button1.");
        if (Math.Abs(restoredForm2Border.Width - 500) > 0.001)
            throw new InvalidOperationException("Form2.Border1 width was overwritten by Form1.Border1.");
        if (Math.Abs(restoredForm2Grid.Width - 600) > 0.001)
            throw new InvalidOperationException("Form2.DataGrid1 width was overwritten by Form1.DataGrid1.");

        if (ReferenceEquals(restoredForm1Button, restoredForm2Button)
            || ReferenceEquals(restoredForm1Border, restoredForm2Border)
            || ReferenceEquals(restoredForm1Grid, restoredForm2Grid))
            throw new InvalidOperationException("Forms share runtime control instances.");

        SwitchToForm(vm, form1.Id);
    }

    private static void AssertMultiFormSameControlNamesPropertyGridEdit(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml");
        RequireGeneratedFile(context, "MainWindow.axaml.cs");
        RequireGeneratedFile(context, "Form2.axaml");
        RequireGeneratedFile(context, "Form2.axaml.cs");
        RequireContains(context.Xaml, "Button Form1", "Form1.Button1 edit should be exported from active form.");
        RequireNotContains(context.Xaml, "Button Form2", "Form1 export should not contain Form2.Button1 edit.");
        var form2File = context.GeneratedFiles.First(file => string.Equals(file.Path, "Form2.axaml", StringComparison.OrdinalIgnoreCase));
        RequireContains(form2File.Content, "Button Form2", "Form2.Button1 edit should be exported.");
        RequireNotContains(form2File.Content, "Button Form1", "Form2 export should not contain Form1.Button1 edit.");
        RequireNotContains(context.DiagnosticsText, "дублирующееся имя control", "Same names across forms should be allowed.");
    }

    private static void RequireSameDefaultNames(DesignControlModel button, DesignControlModel border, DesignControlModel grid)
    {
        if (button.Name != "Button1" || border.Name != "Border1" || grid.Name != "DataGrid1")
            throw new InvalidOperationException($"Expected default per-form names Button1/Border1/DataGrid1, got {button.Name}/{border.Name}/{grid.Name}.");
    }

    private static void ConfigureAddFormSimpleIsolation(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");
        button.Text = "111";
        vm.SelectSingleControl(button);
        vm.MoveSelectedControl(12, 8);

        vm.AddFormEditorCommand?.Execute(null);
        var form2 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form2 missing.");
        if (string.Equals(form2.Id, form1.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("AddForm did not activate a new document.");
        if (vm.Controls.Count != 0)
            throw new InvalidOperationException("New Form2 must be empty.");
        if (vm.SelectedControl is not null || vm.SelectedControlIds.Count != 0)
            throw new InvalidOperationException("AddForm must clear selected control.");
        RequirePropertyGridContext(vm, form2.Id, "");

        vm.SetActiveForm(form1.Id, "AddFormSimpleIsolation");
        if (!string.Equals(vm.ActiveDocumentId, form1.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SetActiveForm did not return to Form1.");
        if (vm.SelectedControl is not null || vm.SelectedControlIds.Count != 0)
            throw new InvalidOperationException("Switching forms must not restore stale selection.");

        var restored = vm.Controls.Single(control => control.Id == button.Id);
        if (!string.Equals(restored.Text, "111", StringComparison.Ordinal))
            throw new InvalidOperationException("Form1 Button text was lost after AddForm.");
        if (Math.Abs(restored.X - 52) > 0.001 || Math.Abs(restored.Y - 48) > 0.001)
            throw new InvalidOperationException("Form1 Button position was lost after AddForm.");
    }

    private static void AssertAddFormSimpleIsolation(SmokeContext context)
    {
        RequireContains(context.Xaml, "Content=\"111\"", "Form1 Button text should survive AddForm.");
        RequireContains(context.Xaml, "Canvas.Left=\"52\"", "Form1 Button X should survive AddForm.");
    }

    private static void ConfigureSwitchFormsClearsInspector(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");
        vm.SelectSingleControl(button);
        RequirePropertyGridContext(vm, form1.Id, button.Id);

        vm.AddFormEditorCommand?.Execute(null);
        var form2 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form2 missing.");
        RequirePropertyGridContext(vm, form2.Id, "");
        if (vm.PropertyGridCategories.SelectMany(category => category.Rows).Any(row => !string.IsNullOrWhiteSpace(row.ContextControlId)))
            throw new InvalidOperationException("Form-level inspector should not keep control rows after AddForm.");

        vm.SetActiveForm(form1.Id, "SwitchFormsClearsInspector");
        RequirePropertyGridContext(vm, form1.Id, "");
        if (vm.SelectedControl is not null || vm.SelectedInteraction is not null)
            throw new InvalidOperationException("Switching forms must clear selected control and logic selection.");
    }

    private static void AssertSwitchFormsClearsInspector(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml");
        RequireNotContains(context.DiagnosticsText, "Stale PropertyGrid", "SwitchFormsClearsInspector should not produce stale PropertyGrid diagnostics.");
    }

    private static void ConfigurePropertyGridTextEditUsesLocalBuffer(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");
        button.Text = "Initial";
        vm.SelectSingleControl(button);

        var row = GetPropertyGridRow(vm, nameof(DesignControlModel.Text));
        vm.BeginPropertyGridTextEdit(row, row.Value);
        vm.UpdatePropertyGridTextEdit(row, "1");
        vm.UpdatePropertyGridTextEdit(row, "12");
        vm.UpdatePropertyGridTextEdit(row, "123456");

        if (!string.Equals(row.Value, "Initial", StringComparison.Ordinal))
            throw new InvalidOperationException("PropertyGrid row.Value changed during live typing before commit.");
        if (!string.Equals(button.Text, "Initial", StringComparison.Ordinal))
            throw new InvalidOperationException("Button.Text changed during live typing before commit.");

        vm.CommitPropertyGridTextEdit(row, "123456");
        if (!string.Equals(button.Text, "123456", StringComparison.Ordinal))
            throw new InvalidOperationException("Button.Text was not committed after local-buffer edit.");

        vm.AddFormEditorCommand?.Execute(null);
        SwitchToForm(vm, form1.Id);
        button = vm.Controls.Single(control => control.Name == "Button1");
        vm.SelectSingleControl(button);
        row = GetPropertyGridRow(vm, nameof(DesignControlModel.Text));
        vm.BeginPropertyGridTextEdit(row, row.Value);
        vm.UpdatePropertyGridTextEdit(row, "222222");

        if (!string.Equals(row.Value, "123456", StringComparison.Ordinal))
            throw new InvalidOperationException("PropertyGrid row.Value changed during live typing after AddForm.");
        if (!string.Equals(button.Text, "123456", StringComparison.Ordinal))
            throw new InvalidOperationException("Button.Text changed during live typing after AddForm.");

        vm.CommitPropertyGridTextEdit(row, "222222");
        if (!string.Equals(button.Text, "222222", StringComparison.Ordinal))
            throw new InvalidOperationException("Button.Text was not committed after AddForm local-buffer edit.");
    }

    private static void AssertPropertyGridTextEditUsesLocalBuffer(SmokeContext context)
    {
        RequireContains(context.Xaml, "Content=\"222222\"", "PropertyGrid local-buffer edit should commit final Button text.");
    }

    private static void ConfigureEditAfterAddFormWorks(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");
        button.Text = "111";

        vm.AddFormEditorCommand?.Execute(null);
        vm.SetActiveForm(form1.Id, "EditAfterAddFormWorks");
        var restored = vm.Controls.Single(control => control.Id == button.Id);
        vm.SelectSingleControl(restored);
        EditPropertyGridTextLikeUi(vm, nameof(DesignControlModel.Text), "222");

        if (!string.Equals(restored.Text, "222", StringComparison.Ordinal))
            throw new InvalidOperationException("PropertyGrid edit after AddForm did not apply.");

        vm.SetActiveForm(vm.CurrentProject.Forms.Single(form => !string.Equals(form.Id, form1.Id, StringComparison.OrdinalIgnoreCase)).Id, "EditAfterAddFormWorks verify Form2");
        if (vm.Controls.Any())
            throw new InvalidOperationException("Form2 should stay empty after editing Form1.");
        vm.SetActiveForm(form1.Id, "EditAfterAddFormWorks export Form1");
    }

    private static void AssertEditAfterAddFormWorks(SmokeContext context)
    {
        RequireContains(context.Xaml, "Content=\"222\"", "Edited Form1 Button text should be exported.");
        var form2File = context.GeneratedFiles.FirstOrDefault(file => string.Equals(file.Path, "Form2.axaml", StringComparison.OrdinalIgnoreCase));
        if (form2File is not null)
            RequireNotContains(form2File.Content, "222", "Form2 should not contain Form1 edited Button text.");
    }

    private static void ConfigureEditTextAfterAddFormStillWorks(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");

        vm.AddFormEditorCommand?.Execute(null);
        SwitchToForm(vm, form1.Id);
        var restored = vm.Controls.Single(control => control.Id == button.Id);
        vm.SelectSingleControl(restored);
        EditPropertyGridTextLikeUi(vm, nameof(DesignControlModel.Text), "222222");

        if (!string.Equals(restored.Text, "222222", StringComparison.Ordinal))
            throw new InvalidOperationException("Text edit after AddForm was not preserved in model.");
    }

    private static void AssertEditTextAfterAddFormStillWorks(SmokeContext context)
    {
        RequireContains(context.Xaml, "Content=\"222222\"", "Text edit after AddForm should be exported.");
    }

    private static void ConfigureChangeForegroundAfterAddFormWorks(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");

        vm.AddFormEditorCommand?.Execute(null);
        SwitchToForm(vm, form1.Id);
        var restored = vm.Controls.Single(control => control.Id == button.Id);
        vm.SelectSingleControl(restored);
        SetPropertyGridValue(vm, nameof(DesignControlModel.Foreground), "#00AA44");

        if (!string.Equals(restored.Foreground, "#00AA44", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Foreground after AddForm was not applied. Got {restored.Foreground}.");
    }

    private static void AssertChangeForegroundAfterAddFormWorks(SmokeContext context)
    {
        RequireContains(context.Xaml, "Foreground=\"#00AA44\"", "Foreground after AddForm should be exported.");
    }

    private static void ConfigureChangeBackgroundAfterAddFormWorks(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");

        vm.AddFormEditorCommand?.Execute(null);
        SwitchToForm(vm, form1.Id);
        var restored = vm.Controls.Single(control => control.Id == button.Id);
        vm.SelectSingleControl(restored);
        SetPropertyGridValue(vm, nameof(DesignControlModel.Background), "#CC99EB");

        if (!string.Equals(restored.Background, "#CC99EB", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Background after AddForm was not applied. Got {restored.Background}.");
    }

    private static void AssertChangeBackgroundAfterAddFormWorks(SmokeContext context)
    {
        RequireContains(context.Xaml, "Background=\"#CC99EB\"", "Background after AddForm should be exported.");
    }

    private static void ConfigureForegroundPersistsInModel(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");
        vm.SelectSingleControl(button);
        SetPropertyGridValue(vm, nameof(DesignControlModel.Foreground), "#123456");

        vm.AddFormEditorCommand?.Execute(null);
        SwitchToForm(vm, form1.Id);
        var restored = vm.Controls.Single(control => control.Id == button.Id);

        if (!string.Equals(restored.Foreground, "#123456", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Foreground was lost after switching forms. Got {restored.Foreground}.");
    }

    private static void AssertForegroundPersistsInModel(SmokeContext context)
    {
        RequireContains(context.Xaml, "Foreground=\"#123456\"", "Persisted Foreground should be exported.");
    }

    private static void ConfigureForegroundAppearsInExportedXaml(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");
        button.Foreground = "#AA2244";
        vm.SelectSingleControl(button);
    }

    private static void AssertForegroundAppearsInExportedXaml(SmokeContext context)
    {
        RequireContains(context.Xaml, "Foreground=\"#AA2244\"", "Button Foreground should appear in exported XAML.");
    }

    private static void ConfigureSameSelectionDoesNotRebuildPropertyGrid(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");
        vm.SelectSingleControl(button);
        vm.InteractionTraceEntries.Clear();

        var sameIdentity = new DesignControlModel
        {
            Id = button.Id,
            Name = button.Name,
            Type = button.Type
        };
        vm.SelectedControl = sameIdentity;

        if (!vm.InteractionTraceEntries.Any(entry => entry.EventName == "SELECTED_CONTROL_SAME_SUPPRESSED"))
            throw new InvalidOperationException("Same selection was not suppressed.");

        if (vm.InteractionTraceEntries.Any(entry =>
            entry.EventName == "RebuildPropertyGrid"
            && entry.Details.Contains("SelectedControlChanged", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Same selection still triggered PropertyGrid rebuild.");
        }
    }

    private static void AssertSameSelectionDoesNotRebuildPropertyGrid(SmokeContext context)
    {
        RequireContains(context.Xaml, "Button1", "Same-selection scenario should keep the selected Button exportable.");
    }

    private static void ConfigurePropertiesPanelVisibleAfterAddFormAndSelectControl(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 90, 70, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");

        vm.AddFormEditorCommand?.Execute(null);
        SwitchToForm(vm, form1.Id);
        var restored = vm.Controls.Single(control => control.Id == button.Id);
        vm.SelectSingleControl(restored);

        if (vm.SelectedControl is null)
            throw new InvalidOperationException("Button selection was not restored before Properties check.");

        if (vm.RightInspectorSelectedIndex != 0)
            throw new InvalidOperationException($"Properties tab is not active. Active index: {vm.RightInspectorSelectedIndex}.");

        RequirePropertyGridRowsContext(vm, form1.Id, button.Id);
        RequirePropertyGridRow(vm, nameof(DesignControlModel.Name));
        RequirePropertyGridRow(vm, nameof(DesignControlModel.Text));
        RequirePropertyGridRow(vm, nameof(DesignControlModel.Width));
        RequirePropertyGridRow(vm, nameof(DesignControlModel.Height));

        if (vm.PropertiesTabRefreshVersion <= 0)
            throw new InvalidOperationException("Properties tab refresh version was not raised after selecting the Button.");

        vm.InteractionTraceEntries.Clear();
        vm.GenerateXaml();

        if (vm.SelectedControl is null || !string.Equals(vm.SelectedControl.Id, button.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Export refresh cleared the selected Button.");

        RequirePropertyGridRowsContext(vm, form1.Id, button.Id);
        RequirePropertyGridRow(vm, nameof(DesignControlModel.Name));
        RequirePropertyGridRow(vm, nameof(DesignControlModel.Text));
        RequirePropertyGridRow(vm, nameof(DesignControlModel.Width));
        RequirePropertyGridRow(vm, nameof(DesignControlModel.Height));

        if (vm.InteractionTraceEntries.Any(entry =>
            entry.EventName == "ApplyDocument"
            || entry.EventName == "APPLY_DOCUMENT_START"
            || entry.EventName == "APPLY_DOCUMENT_END"))
        {
            var applyTrace = string.Join(" | ", vm.InteractionTraceEntries
                .Where(entry => entry.EventName.Contains("APPLY_DOCUMENT", StringComparison.OrdinalIgnoreCase))
                .Select(entry => $"{entry.EventName}:{entry.Details}"));
            throw new InvalidOperationException($"Export refresh still applied editor documents: {applyTrace}");
        }
    }

    private static void AssertPropertiesPanelVisibleAfterAddFormAndSelectControl(SmokeContext context)
    {
        RequireContains(context.Xaml, "Button1", "Properties visibility scenario should keep Button exportable.");
    }

    private static void ConfigureInspectorShowsActiveFormPropertiesAfterSwitch(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        EditPropertyGridTextLikeUi(vm, "FormTitle", "Form_A");
        RequirePropertyGridContext(vm, form1.Id, "");
        RequireNoDuplicatePropertyRows(vm);

        var form2 = vm.CreateNewForm();
        EditPropertyGridTextLikeUi(vm, "FormTitle", "Form_B");
        RequirePropertyGridContext(vm, form2.Id, "");
        RequireNoDuplicatePropertyRows(vm);

        SwitchToForm(vm, form1.Id);
        RequirePropertyGridContext(vm, form1.Id, "");
        var form1TitleRow = GetPropertyGridRow(vm, "FormTitle");
        if (!string.Equals(form1TitleRow.Value, "Form_A", StringComparison.Ordinal))
            throw new InvalidOperationException($"Inspector showed stale form title for Form1. Got {form1TitleRow.Value}.");

        SwitchToForm(vm, form2.Id);
        RequirePropertyGridContext(vm, form2.Id, "");
        var form2TitleRow = GetPropertyGridRow(vm, "FormTitle");
        if (!string.Equals(form2TitleRow.Value, "Form_B", StringComparison.Ordinal))
            throw new InvalidOperationException($"Inspector showed stale form title for Form2. Got {form2TitleRow.Value}.");
    }

    private static void AssertInspectorShowsActiveFormPropertiesAfterSwitch(SmokeContext context)
    {
        RequireContains(context.Xaml, "x:Class", "Inspector active-form scenario should still export.");
    }

    private static void ConfigureRenameFormDoesNotDuplicatePropertyRows(MainWindowViewModel vm)
    {
        EditPropertyGridTextLikeUi(vm, "FormTitle", "RenamedForm");
        RequireNoDuplicatePropertyRows(vm);

        EditPropertyGridTextLikeUi(vm, "FormTitle", "RenamedFormAgain");
        RequireNoDuplicatePropertyRows(vm);

        if (vm.InteractionTraceEntries.Any(entry => entry.EventName == "PROPERTY_GRID_DUPLICATE_ROW_DETECTED"))
        {
            var trace = string.Join(" | ", vm.InteractionTraceEntries
                .Where(entry => entry.EventName == "PROPERTY_GRID_DUPLICATE_ROW_DETECTED")
                .Select(entry => entry.Details));
            throw new InvalidOperationException($"Duplicate PropertyGrid rows were detected after form rename: {trace}");
        }
    }

    private static void AssertRenameFormDoesNotDuplicatePropertyRows(SmokeContext context)
    {
        RequireContains(context.Xaml, "RenamedFormAgain", "Renamed form title should be exported.");
    }

    private static void ConfigureWidthHeightEditDoesNotLockInspector(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Button was not created.");
        vm.SelectSingleControl(button);

        EditPropertyGridTextLikeUi(vm, nameof(DesignControlModel.Width), "222");
        EditPropertyGridTextLikeUi(vm, nameof(DesignControlModel.Height), "64");
        EditPropertyGridTextLikeUi(vm, nameof(DesignControlModel.Text), "After size edit");
        SetPropertyGridValue(vm, nameof(DesignControlModel.Background), "#334455");
        EditPropertyGridTextLikeUi(vm, nameof(DesignControlModel.Name), "ButtonAfterSize");

        if (!string.Equals(button.Name, "ButtonAfterSize", StringComparison.Ordinal)
            || !string.Equals(button.Text, "After size edit", StringComparison.Ordinal)
            || Math.Abs(button.Width - 222) > 0.001
            || Math.Abs(button.Height - 64) > 0.001
            || !string.Equals(button.Background, "#334455", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Width/Height edit left the inspector unable to edit later properties.");
        }

        if (vm.InteractionTraceEntries.Any(entry => entry.EventName == "PROPERTY_EDIT_STATE_STUCK"))
            throw new InvalidOperationException("Width/Height edit left a stuck property edit state.");
    }

    private static void AssertWidthHeightEditDoesNotLockInspector(SmokeContext context)
    {
        RequireContains(context.Xaml, "ButtonAfterSize", "Width/Height lock scenario should export renamed Button.");
        RequireContains(context.Xaml, "Content=\"After size edit\"", "Width/Height lock scenario should export later text edit.");
    }

    private static void ConfigureEmptyButtonTextIsPreserved(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Button was not created.");
        vm.SelectSingleControl(button);
        EditPropertyGridTextLikeUi(vm, nameof(DesignControlModel.Text), "");

        if (!string.Equals(button.Text, "", StringComparison.Ordinal))
            throw new InvalidOperationException($"Empty Button text was not preserved in model. Got '{button.Text}'.");
    }

    private static void AssertEmptyButtonTextIsPreserved(SmokeContext context)
    {
        RequireContains(context.Xaml, "Content=\"\"", "Empty Button text should be exported as an explicit empty Content.");
        RequireNotContains(context.Xaml, "Content=\"Кнопка\"", "Empty Button text should not fall back to the default Russian caption.");
    }

    private static void ConfigurePropertyEditBlockedForStaleInspectorTarget(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var form2 = vm.CreateNewForm();
        SwitchToForm(vm, form1.Id);

        var staleRow = new PropertyGridRowViewModel(
            "FormTitle",
            "Title",
            "Common",
            PropertyGridEditorKind.Text,
            "Form_B",
            "Stale form row.",
            (_, value) => vm.FormTitle = value,
            contextDocumentId: form2.Id,
            contextControlType: "Form");

        vm.CommitPropertyGridEdit(staleRow, "ShouldNotApply");

        if (string.Equals(vm.FormTitle, "ShouldNotApply", StringComparison.Ordinal))
            throw new InvalidOperationException("Stale inspector form edit was applied to the active form.");

        if (!vm.InteractionTraceEntries.Any(entry => entry.EventName == "PROPERTY_EDIT_BLOCKED_STALE_INSPECTOR_TARGET"))
            throw new InvalidOperationException("Stale inspector edit was not logged as blocked.");
    }

    private static void AssertPropertyEditBlockedForStaleInspectorTarget(SmokeContext context)
    {
        RequireNotContains(context.Xaml, "ShouldNotApply", "Stale inspector edit should not appear in exported XAML.");
    }

    private static void ConfigureAddFormRegressionStillWorks(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Button was not created.");

        vm.CreateNewForm();
        SwitchToForm(vm, form1.Id);
        var restored = vm.Controls.Single(control => control.Id == button.Id);
        vm.SelectSingleControl(restored);
        RequirePropertyGridRowsContext(vm, form1.Id, restored.Id);
        EditPropertyGridTextLikeUi(vm, nameof(DesignControlModel.Text), "Regression still works");

        if (!string.Equals(restored.Text, "Regression still works", StringComparison.Ordinal))
            throw new InvalidOperationException("AddForm regression text edit failed.");

        if (vm.InteractionTraceEntries.Any(entry =>
            entry.EventName == "APPLY_DOCUMENT_START"
            && entry.Details.Contains("Property", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Property edit after AddForm triggered ApplyDocument.");
        }
    }

    private static void AssertAddFormRegressionStillWorks(SmokeContext context)
    {
        RequireContains(context.Xaml, "Content=\"Regression still works\"", "AddForm regression should export edited Button text.");
    }

    private static void ConfigureNewProjectClearsPreviousFormsAndState(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var form1Button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");
        form1Button.Text = "Old project Button";
        vm.SelectSingleControl(form1Button);
        RequirePropertyGridRowsContext(vm, form1.Id, form1Button.Id);

        var source = CustomersSource();
        vm.BindingSources.Add(source);
        vm.SelectedBindingSource = source;
        vm.ImportedDllCatalog.Add(new ImportedDllInfoModel
        {
            FileName = "OldProject.dll",
            AssemblyPath = @"C:\Temp\OldProject.dll",
            SourceCount = 1,
            SourceNames = source.Name,
            TypeNames = source.ItemTypeName,
            Summary = "Fake smoke metadata"
        });
        vm.FilteredImportedDllCatalog.Add(vm.ImportedDllCatalog[0]);

        vm.GenerateXaml();
        var form2 = vm.CreateNewForm();
        var form2TextBox = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.TextBox, 70, 80, null, false, form2.Id)
            ?? throw new InvalidOperationException("Form2 TextBox was not created.");
        form2TextBox.PlaceholderText = "Old Form2";
        vm.SelectSingleControl(form2TextBox);

        var form3 = vm.CreateNewForm();
        var form3Border = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Border, 80, 90, null, false, form3.Id)
            ?? throw new InvalidOperationException("Form3 Border was not created.");
        vm.SelectSingleControl(form3Border);

        var oldFormIds = vm.CurrentProject.Forms.Select(form => form.Id).ToList();
        var oldControlIds = vm.CurrentProject.Forms
            .SelectMany(form => form.Document.Controls)
            .Select(control => control.Id)
            .Concat(vm.Controls.Select(control => control.Id))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (oldFormIds.Count < 3 || oldControlIds.Count < 3)
            throw new InvalidOperationException("NewProject reset smoke did not prepare old multi-form state.");

        vm.NewDocumentCommand.Execute(null);

        AssertCleanNewProjectViewModelState(vm, oldFormIds, oldControlIds);
    }

    private static void AssertCleanNewProjectViewModelState(
        MainWindowViewModel vm,
        IReadOnlyList<string> oldFormIds,
        IReadOnlyList<string> oldControlIds)
    {
        if (vm.CurrentProject.Forms.Count != 1 || vm.Forms.Count != 1)
            throw new InvalidOperationException($"New Project should leave one form. Project={vm.CurrentProject.Forms.Count}, VM={vm.Forms.Count}.");

        var newForm = vm.ActiveFormDocument ?? throw new InvalidOperationException("New Project did not create an active Form1.");
        if (!string.Equals(newForm.DisplayName, "Form1", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"New Project active form should be Form1. Got {newForm.DisplayName}.");

        if (oldFormIds.Any(oldId => string.Equals(newForm.Id, oldId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("New Project reused an old Form id.");

        if (newForm.Document.Controls.Count != 0)
            throw new InvalidOperationException($"New Project Form1 document should be empty. Controls={newForm.Document.Controls.Count}.");

        if (vm.Controls.Count != 0)
            throw new InvalidOperationException($"New Project canvas should be empty. Controls={vm.Controls.Count}.");

        if (vm.SelectedControl is not null || vm.SelectedControlIds.Count != 0)
            throw new InvalidOperationException("New Project should clear selected control.");

        if (vm.DocumentTabs.Count != 1 || !string.Equals(vm.DocumentTabs[0].DocumentId, newForm.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("New Project should leave exactly one tab for the new Form1.");

        var explorerFormIds = GetProjectExplorerFormIds(vm).ToList();
        if (explorerFormIds.Count != 1 || !string.Equals(explorerFormIds[0], newForm.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Project Explorer should show exactly the new Form1. Explorer forms: {string.Join(", ", explorerFormIds)}.");

        var formsFolder = FlattenExplorerItems(vm.ProjectExplorerItems).FirstOrDefault(item => string.Equals(item.TargetId, "Forms", StringComparison.OrdinalIgnoreCase));
        if (formsFolder is null || formsFolder.Count != 1)
            throw new InvalidOperationException($"Project Explorer Forms folder should show count 1. Got {formsFolder?.Count.ToString() ?? "missing"}.");

        if (vm.BindingSources.Count != 0 || vm.ImportedDllCatalog.Count != 0 || vm.FilteredImportedDllCatalog.Count != 0)
            throw new InvalidOperationException("New Project should clear data sources and imported DLL metadata.");

        if (vm.HasUndo || vm.HasRedo)
            throw new InvalidOperationException("New Project should clear undo/redo state.");

        if (vm.UndoRedoHistoryItems.Any(item =>
            oldFormIds.Any(oldId => item.Snapshot.Contains(oldId, StringComparison.OrdinalIgnoreCase))
            || oldControlIds.Any(oldId => item.Snapshot.Contains(oldId, StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidOperationException("New Project history snapshots still contain old form/control ids.");
        }

        if (vm.GeneratedFiles.Count != 0 || vm.RequiredPackages.Count != 0 || vm.ExportDiagnostics.Count != 0)
            throw new InvalidOperationException("New Project should clear generated export pipeline state.");

        var leakedFormIds = oldFormIds.Where(oldId =>
            vm.CurrentProject.Forms.Any(form => string.Equals(form.Id, oldId, StringComparison.OrdinalIgnoreCase))
            || vm.Forms.Any(form => string.Equals(form.Id, oldId, StringComparison.OrdinalIgnoreCase))
            || vm.DocumentTabs.Any(tab => string.Equals(tab.DocumentId, oldId, StringComparison.OrdinalIgnoreCase))
            || vm.Workspace.Session.OpenDocumentIds.Any(id => string.Equals(id, oldId, StringComparison.OrdinalIgnoreCase))
            || string.Equals(vm.Workspace.Session.ActiveDocumentId, oldId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (leakedFormIds.Count > 0)
            throw new InvalidOperationException($"Old form ids leaked after New Project: {string.Join(", ", leakedFormIds)}.");

        var leakedControlIds = oldControlIds.Where(oldId =>
            vm.Controls.Any(control => string.Equals(control.Id, oldId, StringComparison.OrdinalIgnoreCase))
            || vm.SelectedControlIds.Any(id => string.Equals(id, oldId, StringComparison.OrdinalIgnoreCase))
            || string.Equals(vm.SelectedControl?.Id, oldId, StringComparison.OrdinalIgnoreCase)
            || vm.PropertyGridCategories.SelectMany(category => category.Rows).Any(row => string.Equals(row.ContextControlId, oldId, StringComparison.OrdinalIgnoreCase))).ToList();
        if (leakedControlIds.Count > 0)
            throw new InvalidOperationException($"Old control ids leaked after New Project: {string.Join(", ", leakedControlIds)}.");

        if (vm.InteractionTraceEntries.Any(entry => entry.EventName == "NEW_PROJECT_LEAKED_OLD_STATE"))
        {
            var leakTrace = string.Join(" | ", vm.InteractionTraceEntries
                .Where(entry => entry.EventName == "NEW_PROJECT_LEAKED_OLD_STATE")
                .Select(entry => entry.Details));
            throw new InvalidOperationException($"New Project leak trace was emitted: {leakTrace}");
        }
    }

    private static void AssertNewProjectClearsPreviousFormsAndState(SmokeContext context)
    {
        RequireContains(context.Xaml, "<Canvas", "New Project should still export a clean empty Form1 canvas.");
        RequireNotContains(context.Xaml, "Old project Button", "Old project controls must not appear in new project export.");
        RequireNotContains(context.Xaml, "Old Form2", "Old Form2 controls must not appear in new project export.");
        if (context.ViewModel.CurrentProject.Forms.Count != 1 || context.ViewModel.Controls.Count != 0)
            throw new InvalidOperationException("NewProjectClearsPreviousFormsAndState should finish with one empty Form1.");
    }

    private static void ConfigureNewProjectAfterSavedMultiFormProjectClearsAllOldForms(MainWindowViewModel vm)
    {
        var state = PrepareSavedTwoFormProjectForNewCommand(vm);
        vm.NewDocumentCommand.Execute(null);
        AssertCleanNewProjectViewModelState(vm, state.OldFormIds, state.OldControlIds);
    }

    private static void AssertNewProjectAfterSavedMultiFormProjectClearsAllOldForms(SmokeContext context)
    {
        RequireContains(context.Xaml, "<Canvas", "New Project after saved multi-form project should export clean Form1.");
        RequireNotContains(context.Xaml, "OldForm1Button", "Old Form1 Button must not be exported after New.");
        RequireNotContains(context.Xaml, "OldForm2Button", "Old Form2 Button must not be exported after New.");
        if (GetProjectExplorerFormIds(context.ViewModel).Count() != 1)
            throw new InvalidOperationException("Project Explorer should contain one form node after New.");
    }

    private static void ConfigureNewProjectDoesNotReuseOldFormNamesOrIds(MainWindowViewModel vm)
    {
        var state = PrepareSavedTwoFormProjectForNewCommand(vm);
        vm.NewDocumentCommand.Execute(null);

        AssertCleanNewProjectViewModelState(vm, state.OldFormIds, state.OldControlIds);

        var forms = vm.CurrentProject.Forms.ToList();
        if (forms.Count(form => string.Equals(form.DisplayName, "Form1", StringComparison.OrdinalIgnoreCase)) != 1)
            throw new InvalidOperationException($"New Project should have exactly one Form1. Forms: {string.Join(", ", forms.Select(form => $"{form.DisplayName}:{form.Id}"))}");

        var newForm = forms[0];
        if (state.OldFormIds.Any(oldId => string.Equals(newForm.Id, oldId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("New Project reused an old form id.");
        if (state.OldControlIds.Any(oldId => newForm.Document.Controls.Any(control => string.Equals(control.Id, oldId, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("New Project reused old controls inside the new Form1 document.");
    }

    private static void AssertNewProjectDoesNotReuseOldFormNamesOrIds(SmokeContext context)
    {
        if (context.ViewModel.CurrentProject.Forms.Count != 1)
            throw new InvalidOperationException("NewProjectDoesNotReuseOldFormNamesOrIds should finish with one form.");
        RequireNotContains(context.Xaml, "OldForm1Button", "Old Form1 Button should not remain after New.");
        RequireNotContains(context.Xaml, "OldForm2Button", "Old Form2 Button should not remain after New.");
    }

    private static void ConfigureNewProjectReleasesOldFormsAndControls(MainWindowViewModel vm)
    {
        var state = PrepareWeakReferenceProjectAndRunNew(vm);
        ForceFullGc();

        var alive = state.WeakReferences
            .Where(item => item.Reference.IsAlive)
            .Select(item => $"{item.Kind}:{item.Id}")
            .ToList();
        if (alive.Count > 0)
            throw new InvalidOperationException($"Old project objects are still alive after New Project: {string.Join(", ", alive.Take(12))}");

        AssertCleanNewProjectViewModelState(vm, state.OldFormIds, state.OldControlIds);
    }

    private static void AssertNewProjectReleasesOldFormsAndControls(SmokeContext context)
    {
        RequireContains(context.Xaml, "<Canvas", "NewProjectReleasesOldFormsAndControls should export clean Form1.");
        RequireNotContains(context.Xaml, "WeakOldForm", "Old weak-reference controls must not be exported after New.");
    }

    private static NewProjectWeakState PrepareWeakReferenceProjectAndRunNew(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var form1Button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 44, 44, null, false, form1.Id)
            ?? throw new InvalidOperationException("Old Form1 Button was not created.");
        form1Button.Name = "WeakOldForm1Button";
        vm.SelectSingleControl(form1Button);

        var form2 = vm.CreateNewForm();
        var form2Button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 84, 84, null, false, form2.Id)
            ?? throw new InvalidOperationException("Old Form2 Button was not created.");
        form2Button.Name = "WeakOldForm2Button";

        var oldFormIds = vm.CurrentProject.Forms.Select(form => form.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        var oldControlIds = vm.CurrentProject.Forms
            .SelectMany(form => form.Document.Controls)
            .Select(control => control.Id)
            .Concat(vm.Controls.Select(control => control.Id))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var weakReferences = new List<NamedWeakReference>
        {
            new("Form", form1.Id, new WeakReference(form1)),
            new("Form", form2.Id, new WeakReference(form2)),
            new("RuntimeControl", form1Button.Id, new WeakReference(form1Button)),
            new("RuntimeControl", form2Button.Id, new WeakReference(form2Button))
        };
        weakReferences.AddRange(vm.PropertyGridCategories
            .SelectMany(category => category.Rows)
            .Take(8)
            .Select(row => new NamedWeakReference("PropertyRow", $"{row.ContextDocumentId}/{row.ContextControlId}/{row.Key}", new WeakReference(row))));

        vm.NewDocumentCommand.Execute(null);
        return new NewProjectWeakState(oldFormIds, oldControlIds, weakReferences);
    }

    private static void ConfigureNewProjectClearsDataDllExportCaches(MainWindowViewModel vm)
    {
        var source = CustomersSource();
        vm.BindingSources.Add(source);
        vm.SelectedBindingSource = source;
        vm.ImportedDllCatalog.Add(new ImportedDllInfoModel
        {
            FileName = "HeavyMetadata.dll",
            AssemblyPath = @"C:\Temp\HeavyMetadata.dll",
            SourceCount = 1,
            SourceNames = source.Name,
            TypeNames = source.ItemTypeName,
            Summary = "Fake heavy metadata"
        });
        vm.FilteredImportedDllCatalog.Add(vm.ImportedDllCatalog[0]);
        vm.Controls.Add(DataGrid("OldDataGrid", source.Id, 40, 40, 520, 260));
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        vm.GenerateXaml();

        if (vm.BindingSources.Count == 0 || vm.ImportedDllCatalog.Count == 0 || vm.GeneratedFiles.Count == 0)
            throw new InvalidOperationException("Data/DLL/export cache smoke setup did not create cache state.");

        var oldFormIds = vm.CurrentProject.Forms.Select(form => form.Id).ToList();
        var oldControlIds = vm.Controls.Select(control => control.Id).ToList();
        vm.NewDocumentCommand.Execute(null);
        AssertCleanNewProjectViewModelState(vm, oldFormIds, oldControlIds);
    }

    private static void AssertNewProjectClearsDataDllExportCaches(SmokeContext context)
    {
        if (context.ViewModel.BindingSources.Count != 0
            || context.ViewModel.ImportedDllCatalog.Count != 0
            || context.ViewModel.FilteredImportedDllCatalog.Count != 0)
        {
            throw new InvalidOperationException("New Project should clear Data/DLL caches.");
        }

        RequireNotContains(context.Xaml, "OldDataGrid", "Old DataGrid should not remain after New Project.");
    }

    private static void ConfigurePropertyEditDoesNotTriggerExportPipeline(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 60, 60, null, false, form1.Id)
            ?? throw new InvalidOperationException("Button was not created.");
        vm.SelectSingleControl(button);
        vm.InteractionTraceEntries.Clear();

        SetPropertyGridValue(vm, nameof(DesignControlModel.Text), "No export storm");

        var forbidden = vm.InteractionTraceEntries
            .Where(entry => entry.EventName is "RefreshExportPipelineResult" or "APPLY_DOCUMENT_START" or "APPLY_DOCUMENT_END"
                || entry.EventName.Contains("BuildSecondaryFormGeneratedFiles", StringComparison.OrdinalIgnoreCase)
                || entry.Details.Contains("BuildSecondaryFormGeneratedFiles", StringComparison.OrdinalIgnoreCase)
                || entry.Details.Contains("RefreshExportPipelineResult", StringComparison.OrdinalIgnoreCase))
            .Select(entry => $"{entry.EventName}:{entry.Details}")
            .ToList();
        if (forbidden.Count > 0)
            throw new InvalidOperationException($"Property edit triggered heavy export/apply pipeline: {string.Join(" | ", forbidden)}");
    }

    private static void AssertPropertyEditDoesNotTriggerExportPipeline(SmokeContext context)
    {
        RequireContains(context.Xaml, "No export storm", "Property edit should still persist before final explicit export.");
    }

    private static void ConfigureExportDoesNotMutateEditorState(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 60, 60, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");
        button.Text = "Export state Button";
        var form2 = vm.CreateNewForm();
        var textBox = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.TextBox, 90, 90, null, false, form2.Id)
            ?? throw new InvalidOperationException("Form2 TextBox was not created.");
        textBox.PlaceholderText = "Secondary";

        SwitchToForm(vm, form1.Id);
        var restoredButton = vm.Controls.Single(control => control.Id == button.Id);
        vm.SelectSingleControl(restoredButton);
        var before = CaptureEditorState(vm);
        vm.InteractionTraceEntries.Clear();

        vm.GenerateXaml();

        var after = CaptureEditorState(vm);
        if (!string.Equals(before, after, StringComparison.Ordinal))
            throw new InvalidOperationException($"Export mutated editor state. Before={before}; After={after}");
        RequireNoExportEditorMutationTrace(vm);
        RequireNoApplyDocumentTrace(vm, "ExportDoesNotMutateEditorState");
    }

    private static void AssertExportDoesNotMutateEditorState(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml");
        RequireSecondaryGeneratedFiles(context);
    }

    private static void ConfigureBuildSecondaryFormsDoesNotCallApplyDocument(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        _ = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 50, 50, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");
        var form2 = vm.CreateNewForm();
        _ = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.TextBlock, 80, 80, null, false, form2.Id)
            ?? throw new InvalidOperationException("Form2 TextBlock was not created.");

        SwitchToForm(vm, form1.Id);
        vm.InteractionTraceEntries.Clear();
        vm.GenerateXaml();

        RequireNoApplyDocumentTrace(vm, "BuildSecondaryFormsDoesNotCallApplyDocument");
        if (!vm.InteractionTraceEntries.Any(entry => entry.EventName == "BUILD_SECONDARY_FORM_GENERATION_PURE"))
            throw new InvalidOperationException("Secondary form export did not use the pure generation path.");
    }

    private static void AssertBuildSecondaryFormsDoesNotCallApplyDocument(SmokeContext context)
    {
        RequireSecondaryGeneratedFiles(context);
    }

    private static void ConfigureExportWhileEditingPropertyDoesNotResetTextBox(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 60, 60, null, false, form1.Id)
            ?? throw new InvalidOperationException("Button was not created.");
        vm.SelectSingleControl(button);
        var row = GetPropertyGridRow(vm, nameof(DesignControlModel.Text));
        var originalRowValue = row.Value;

        vm.BeginPropertyGridTextEdit(row, row.Value);
        vm.UpdatePropertyGridTextEdit(row, "Typing during export");
        vm.InteractionTraceEntries.Clear();

        vm.GenerateXaml();

        if (!string.Equals(row.Value, originalRowValue, StringComparison.Ordinal))
            throw new InvalidOperationException($"Export reset the property row during active edit. Expected '{originalRowValue}', got '{row.Value}'.");
        if (vm.SelectedControl is null || !string.Equals(vm.SelectedControl.Id, button.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Export changed selected control during property edit.");
        if (vm.InteractionTraceEntries.Any(entry => entry.EventName == "RebuildPropertyGrid"))
            throw new InvalidOperationException("Export rebuilt PropertyGrid during property edit.");
        RequireNoApplyDocumentTrace(vm, "ExportWhileEditingPropertyDoesNotResetTextBox");

        vm.CommitPropertyGridTextEdit(row, "Typing committed after export");
    }

    private static void AssertExportWhileEditingPropertyDoesNotResetTextBox(SmokeContext context)
    {
        RequireContains(context.Xaml, "Content=\"Typing committed after export\"", "Property edit should commit after export without reset.");
    }

    private static void ConfigureValidateBuildDoesNotChangeActiveForm(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 60, 60, null, false, form1.Id)
            ?? throw new InvalidOperationException("Button was not created.");
        var form2 = vm.CreateNewForm();
        _ = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.TextBox, 90, 90, null, false, form2.Id)
            ?? throw new InvalidOperationException("Form2 TextBox was not created.");

        SwitchToForm(vm, form1.Id);
        var restoredButton = vm.Controls.Single(control => control.Id == button.Id);
        vm.SelectSingleControl(restoredButton);
        var before = CaptureEditorState(vm);
        vm.InteractionTraceEntries.Clear();

        var validationRoot = Path.Combine(Path.GetTempPath(), "FormDesignerSmokeValidation", Guid.NewGuid().ToString("N"));
        var result = vm.ValidateCurrentExportBuildAsync(validationRoot).GetAwaiter().GetResult();
        if (result.Status != ExportBuildValidationStatus.Passed)
            throw new InvalidOperationException($"Validation build did not pass: {result.Status} {result.Output}");

        var after = CaptureEditorState(vm);
        if (!string.Equals(before, after, StringComparison.Ordinal))
            throw new InvalidOperationException($"Validate build mutated editor state. Before={before}; After={after}");
        RequireNoExportEditorMutationTrace(vm);
        RequireNoApplyDocumentTrace(vm, "ValidateBuildDoesNotChangeActiveForm");
    }

    private static void AssertValidateBuildDoesNotChangeActiveForm(SmokeContext context)
    {
        RequireSecondaryGeneratedFiles(context);
    }

    private static void ConfigureExportAfterNewProjectDoesNotUseOldDocuments(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var oldButton = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Old Button was not created.");
        oldButton.Name = "OldExportButton";
        oldButton.Text = "Old export text";
        var oldForm2 = vm.CreateNewForm();
        var oldTextBox = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.TextBox, 80, 80, null, false, oldForm2.Id)
            ?? throw new InvalidOperationException("Old TextBox was not created.");
        oldTextBox.Name = "OldExportTextBox";

        vm.NewDocumentCommand.Execute(null);
        vm.InteractionTraceEntries.Clear();
        vm.GenerateXaml();

        if (vm.GeneratedFiles.Any(file => file.Path.Contains("Form2", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Export after New Project still generated old secondary form files.");
        if (vm.GeneratedFiles.Any(file => file.Content.Contains("OldExport", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Export after New Project still contains old document controls.");
        RequireNoApplyDocumentTrace(vm, "ExportAfterNewProjectDoesNotUseOldDocuments");
    }

    private static void AssertExportAfterNewProjectDoesNotUseOldDocuments(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml");
        if (context.GeneratedFiles.Any(file => file.Path.Contains("Form2", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Final export after New Project should not include old Form2.");
        RequireNotContains(context.Xaml, "OldExport", "Final export after New Project should not include old controls.");
    }

    private static void ConfigureResizeDoesNotRebuildPropertyGridOnEveryMove(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 80, 80, null, false, form1.Id)
            ?? throw new InvalidOperationException("Button was not created.");
        vm.SelectSingleControl(button);
        vm.InteractionTraceEntries.Clear();

        for (var index = 0; index < 30; index++)
            vm.MoveSelectedControl(1, 1);

        var rebuilds = vm.InteractionTraceEntries.Count(entry => entry.EventName == "RebuildPropertyGrid");
        if (rebuilds > 2)
            throw new InvalidOperationException($"Resize/move should not rebuild PropertyGrid on every move. Rebuilds={rebuilds}.");
    }

    private static void AssertResizeDoesNotRebuildPropertyGridOnEveryMove(SmokeContext context)
    {
        RequireContains(context.Xaml, "Button1", "Resize/move storm scenario should keep the Button exportable.");
    }

    private static NewProjectOldState PrepareSavedTwoFormProjectForNewCommand(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var form1Button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 42, 42, null, false, form1.Id)
            ?? throw new InvalidOperationException("Old Form1 Button was not created.");
        form1Button.Name = "OldForm1Button";
        form1Button.Text = "Old Form1 Button";

        var form2 = vm.CreateNewForm();
        var form2Button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 82, 88, null, false, form2.Id)
            ?? throw new InvalidOperationException("Old Form2 Button was not created.");
        form2Button.Name = "OldForm2Button";
        form2Button.Text = "Old Form2 Button";

        var oldJson = vm.ExportWorkspaceJson();
        if (!oldJson.Contains("OldForm1Button", StringComparison.Ordinal) || !oldJson.Contains("OldForm2Button", StringComparison.Ordinal))
            throw new InvalidOperationException("Saved multi-form smoke workspace did not contain both old buttons.");
        vm.MarkDocumentSaved(Path.Combine(Path.GetTempPath(), $"old-project-{Guid.NewGuid():N}.formdesigner.json"));

        var oldFormIds = vm.CurrentProject.Forms.Select(form => form.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        var oldControlIds = vm.CurrentProject.Forms
            .SelectMany(form => form.Document.Controls)
            .Select(control => control.Id)
            .Concat(vm.Controls.Select(control => control.Id))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (oldFormIds.Count != 2 || oldControlIds.Count < 2)
            throw new InvalidOperationException("Saved multi-form smoke setup did not prepare two old forms and controls.");

        return new NewProjectOldState(oldFormIds, oldControlIds);
    }

    private static void ConfigureDragAfterAddFormWorks(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");

        vm.AddFormEditorCommand?.Execute(null);
        vm.SetActiveForm(form1.Id, "DragAfterAddFormWorks");
        var restored = vm.Controls.Single(control => control.Id == button.Id);
        vm.SelectSingleControl(restored);
        vm.MoveSelectedControl(25, 15);

        if (Math.Abs(restored.X - 65) > 0.001 || Math.Abs(restored.Y - 55) > 0.001)
            throw new InvalidOperationException($"Move after AddForm did not apply. Got {restored.X}/{restored.Y}.");

        vm.SetActiveForm(vm.CurrentProject.Forms.Single(form => !string.Equals(form.Id, form1.Id, StringComparison.OrdinalIgnoreCase)).Id, "DragAfterAddFormWorks verify Form2");
        vm.SetActiveForm(form1.Id, "DragAfterAddFormWorks verify Form1");
        restored = vm.Controls.Single(control => control.Id == button.Id);
        if (Math.Abs(restored.X - 65) > 0.001 || Math.Abs(restored.Y - 55) > 0.001)
            throw new InvalidOperationException("Moved position was lost after switching forms.");
    }

    private static void AssertDragAfterAddFormWorks(SmokeContext context)
    {
        RequireContains(context.Xaml, "Canvas.Left=\"65\"", "Moved Button X should be exported after AddForm.");
        RequireContains(context.Xaml, "Canvas.Top=\"55\"", "Moved Button Y should be exported after AddForm.");
    }

    private static void ConfigureAddEmptySecondFormDoesNotBreakFirstFormPropertyGrid(MainWindowViewModel vm)
    {
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");

        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 32, 42, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");
        var border = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Border, 32, 104, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Border was not created.");
        var grid = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.DataGrid, 240, 42, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 DataGrid was not created.");

        button.Name = "Button1";
        border.Name = "Border1";
        grid.Name = "DataGrid1";

        vm.SelectSingleControl(border);
        SetPropertyGridValue(vm, nameof(DesignControlModel.Width), "300");
        RequirePropertyGridContext(vm, form1.Id, border.Id);

        vm.AddFormEditorCommand?.Execute(null);
        var form2 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form2 missing.");
        if (form2.Id == form1.Id)
            throw new InvalidOperationException("AddForm did not switch to a new form.");
        if (vm.Controls.Count != 0)
            throw new InvalidOperationException("New Form2 should be empty in this regression scenario.");

        SwitchToForm(vm, form1.Id);
        if (vm.ActiveDocumentId != form1.Id)
            throw new InvalidOperationException("ActiveDocumentId did not return to Form1.");

        RequireActiveControls(vm, "Button1", "Border1", "DataGrid1");
        var restoredBorder = vm.Controls.Single(control => control.Name == "Border1");
        vm.SelectSingleControl(restoredBorder);
        RequirePropertyGridContext(vm, form1.Id, restoredBorder.Id);
        SetPropertyGridValue(vm, nameof(DesignControlModel.Width), "500");
        if (Math.Abs(restoredBorder.Width - 500) > 0.001)
            throw new InvalidOperationException("Form1.Border1 Width edit did not apply after adding empty Form2.");

        var restoredButton = vm.Controls.Single(control => control.Name == "Button1");
        vm.SelectSingleControl(restoredButton);
        RequirePropertyGridContext(vm, form1.Id, restoredButton.Id);
        SetPropertyGridValue(vm, nameof(DesignControlModel.Text), "Button after empty Form2");
        if (restoredButton.Text != "Button after empty Form2")
            throw new InvalidOperationException("Form1.Button1 Text edit did not apply after adding empty Form2.");

        if (vm.OutputEntries.Any(entry => entry.Message.Contains("Rejected stale PropertyGrid edit", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A stale PropertyGrid edit was rejected during the empty Form2 regression.");
    }

    private static void AssertAddEmptySecondFormDoesNotBreakFirstFormPropertyGrid(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml");
        RequireGeneratedFile(context, "MainWindow.axaml.cs");
        RequireContains(context.Xaml, "Button after empty Form2", "Form1 Button edit after empty Form2 should be exported.");
        RequireContains(context.Xaml, "Width=\"500\"", "Form1 Border width edit after empty Form2 should be exported.");
        var form2File = context.GeneratedFiles.FirstOrDefault(file => string.Equals(file.Path, "Form2.axaml", StringComparison.OrdinalIgnoreCase));
        if (form2File is not null)
            RequireNotContains(form2File.Content, "Button after empty Form2", "Empty Form2 export should not contain Form1 Button.");
        RequireNotContains(context.DiagnosticsText, "Document isolation", "Empty Form2 workflow should not produce document isolation diagnostics.");
    }

    private static void ConfigureAddEmptySecondFormDoesNotBreakFirstFormInspectorAndLogic(MainWindowViewModel vm)
    {
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");

        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 32, 42, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");
        var border = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Border, 32, 104, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Border was not created.");
        var grid = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.DataGrid, 240, 42, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 DataGrid was not created.");

        button.Name = "Button1";
        button.Text = "Before second form";
        border.Name = "Border1";
        grid.Name = "DataGrid1";

        vm.SelectSingleControl(button);
        RequirePropertyGridContext(vm, form1.Id, button.Id);
        RequirePropertyGridRowsContext(vm, form1.Id, button.Id);

        vm.AddFormEditorCommand?.Execute(null);
        var form2 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form2 missing.");
        if (form2.Id == form1.Id)
            throw new InvalidOperationException("AddForm did not switch to a new form.");
        if (vm.Controls.Count != 0)
            throw new InvalidOperationException("New Form2 should be empty.");

        SwitchToForm(vm, form1.Id);
        if (!string.Equals(vm.ActiveDocumentId, form1.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("ActiveDocumentId did not return to Form1.");

        var restoredButton = vm.Controls.Single(control => control.Name == "Button1");
        vm.SelectSingleControl(restoredButton);
        RequireInspectorSelection(vm, form1.Id, restoredButton.Id, "Button1");
        EditPropertyGridTextLikeUi(vm, nameof(DesignControlModel.Name), "OpenDetailsButton");
        if (!string.Equals(restoredButton.Name, "OpenDetailsButton", StringComparison.Ordinal))
            throw new InvalidOperationException("Button.Name edit did not apply to Form1 selected control.");

        RequireInspectorSelection(vm, form1.Id, restoredButton.Id, "OpenDetailsButton");
        EditPropertyGridTextLikeUi(vm, nameof(DesignControlModel.Text), "Open details");
        if (!string.Equals(restoredButton.Text, "Open details", StringComparison.Ordinal))
            throw new InvalidOperationException("Button.Text edit did not apply to Form1 selected control.");

        var restoredBorder = vm.Controls.Single(control => control.Name == "Border1");
        vm.SelectSingleControl(restoredBorder);
        RequireInspectorSelection(vm, form1.Id, restoredBorder.Id, "Border1");
        EditPropertyGridTextLikeUi(vm, nameof(DesignControlModel.Width), "444");
        if (Math.Abs(restoredBorder.Width - 444) > 0.001)
            throw new InvalidOperationException("Border.Width edit did not apply to Form1 selected control.");

        vm.SelectSingleControl(restoredButton);
        RequireInspectorSelection(vm, form1.Id, restoredButton.Id, "OpenDetailsButton");
        vm.AddShowMessageInteractionForSelectedCommand.Execute(null);
        if (vm.SelectedInteraction is null)
            throw new InvalidOperationException("Button Logic quick action did not create an interaction.");
        if (!string.Equals(vm.WorkspaceMode, MainWindowViewModel.WorkspaceModeLogic, StringComparison.Ordinal))
            throw new InvalidOperationException("Button Logic quick action should keep the Logic workspace active.");
        if (!string.Equals(vm.SelectedInteraction.SourceControlName, restoredButton.Name, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(vm.SelectedInteraction.EventName, InteractionModel.EventButtonClick, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(vm.SelectedInteraction.ActionType, InteractionModel.ActionShowMessage, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Button Logic interaction was created with an unexpected source/event/action.");
        }

        if (vm.OutputEntries.Any(entry => entry.Message.Contains("Rejected stale PropertyGrid edit", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A stale PropertyGrid edit was rejected during the inspector/logic regression.");
    }

    private static void AssertAddEmptySecondFormDoesNotBreakFirstFormInspectorAndLogic(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml");
        RequireGeneratedFile(context, "MainWindow.axaml.cs");
        RequireContains(context.Xaml, "x:Name=\"OpenDetailsButton\"", "Button.Name edit after empty Form2 should be exported.");
        RequireContains(context.Xaml, "Content=\"Open details\"", "Button.Text edit after empty Form2 should be exported.");
        RequireContains(context.Xaml, "Click=\"OpenDetailsButtonClick\"", "Button.Click logic should be exported.");
        RequireContains(context.CSharp, "private async void OpenDetailsButtonClick", "Button.Click ShowMessage handler should be generated.");
        RequireContains(context.Xaml, "Width=\"444\"", "Border.Width edit after empty Form2 should be exported.");
        RequireNotContains(context.DiagnosticsText, "Document isolation", "Inspector/Logic regression should not produce document isolation diagnostics.");
    }

    private static void ConfigureAlphaEndToEndProjectExport(MainWindowViewModel vm)
    {
        ConfigureAlphaProject(vm);
    }

    private static void AssertAlphaEndToEndProjectExport(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml");
        RequireGeneratedFile(context, "MainWindow.axaml.cs");
        RequireGeneratedFile(context, "Form2.axaml");
        RequireGeneratedFile(context, "Form2.axaml.cs");
        RequireContains(context.Xaml, "Click=\"OpenForm2ButtonClick\"", "Alpha OpenForm handler should be wired in XAML.");
        RequireContains(context.Xaml, "SelectionChanged=\"CustomersGrid_SelectionChanged\"", "Alpha DataGrid.SelectionChanged handler should be wired in XAML.");
        RequireContains(context.Xaml, "Checked=\"DetailsCheckBox_Checked\"", "Alpha CheckBox.Checked handler should be wired in XAML.");
        RequireContains(context.CSharp, "var windowForm2 = new Form2();", "Alpha OpenForm handler should instantiate Form2.");
        RequireContains(context.CSharp, "SelectedNameTextBox.Text = ResolveInteractionValue(selectedItem, @\"Name\", @\"\", @\"Empty\");", "Alpha DataGrid selection should fill TextBox from Name.");
        RequireContains(context.ChecklistText, "Forms exported: 2/2", "Alpha export should include both forms.");
        RequireContains(context.ChecklistText, "Interactions exported: 4/4", "Alpha export should include all configured interactions.");
        RequireContains(context.ChecklistText, "OpenForm interactions: 1", "Alpha checklist should report OpenForm.");
        RequireContains(context.ChecklistText, "Avalonia.Controls.DataGrid", "Alpha Real DataGrid export should require DataGrid NuGet.");
    }

    private static void ConfigureDataGridBindingSourceWorkflow(MainWindowViewModel vm)
    {
        var source = CustomersSource();
        vm.BindingSources.Add(source);
        vm.SelectedBindingSource = source;
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;

        var grid = DataGrid("CustomersGrid", source.Id, 34, 48, 650, 300);
        grid.ShowFilterRow = true;
        grid.ShowGroupPanel = true;
        grid.AllowGrouping = true;
        vm.Controls.Add(grid);
    }

    private static void AssertDataGridBindingSourceWorkflow(SmokeContext context)
    {
        RequireContains(context.Xaml, "<DataGrid", "BindingSource workflow should export a real DataGrid.");
        RequireContains(context.Xaml, "Binding=\"{Binding Id}\"", "DataGrid should include BindingSource field Id.");
        RequireContains(context.Xaml, "Binding=\"{Binding Name}\"", "DataGrid should include BindingSource field Name.");
        RequireContains(context.Xaml, "Binding=\"{Binding Email}\"", "DataGrid should include BindingSource field Email.");
        RequireContains(context.Xaml, "Binding=\"{Binding Status}\"", "DataGrid should include BindingSource field Status.");
        RequireNotContains(context.Xaml, "<dataGrid:DataGridTextColumn", "BindingSource workflow should not use prefixed DataGridTextColumn.");
        RequireNotContains(context.Xaml, "{Binding }", "BindingSource workflow should not generate empty bindings.");
        RequireContains(context.ChecklistText, "DataGrid: Real Avalonia DataGrid", "Checklist should keep Real DataGrid mode.");
        RequireContains(context.ChecklistText, "Avalonia.Controls.DataGrid", "Checklist should report DataGrid package.");
        RequireNotContains(context.DiagnosticsText, "DataGrid without fields", "DataGrid with real BindingSource fields must not warn about missing fields.");
    }

    private static void ConfigureOpenFormPreviewAndExport(MainWindowViewModel vm)
    {
        ConfigureTwoFormOpenFormProject(vm);
    }

    private static void AssertOpenFormPreviewAndExport(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml");
        RequireGeneratedFile(context, "MainWindow.axaml.cs");
        RequireGeneratedFile(context, "Form2.axaml");
        RequireGeneratedFile(context, "Form2.axaml.cs");
        RequireContains(context.Xaml, "Click=\"OpenForm2ButtonClick\"", "OpenForm preview/export smoke should wire Button.Click.");
        RequireContains(context.CSharp, "var windowForm2 = new Form2();", "OpenForm export should create Form2.");
        RequireContains(context.CSharp, "windowForm2.Show();", "OpenForm export should call Show.");
        RequireNotContains(context.DiagnosticsText, "OpenForm target form not found", "OpenForm target should be valid.");
        RequireNotContains(context.DiagnosticsText, "OpenForm target form не выбран", "OpenForm target should be selected.");
    }

    private static void ConfigurePropertyGridDataGridSetup(MainWindowViewModel vm)
    {
        ConfigureDataGridBindingSourceWorkflow(vm);
        var grid = vm.Controls.First(control => string.Equals(control.Name, "CustomersGrid", StringComparison.OrdinalIgnoreCase));
        vm.SelectControls(new[] { grid }, grid);
        vm.WorkspaceMode = MainWindowViewModel.WorkspaceModeData;

        RequirePropertyGridRow(vm, "Width");
        RequirePropertyGridRow(vm, "Height");
        RequirePropertyGridRow(vm, "HeaderBackground");
        if (vm.SelectedBindingSourceForControl is null || vm.SelectedBindingSourceForControl.Fields.Count != 4)
            throw new InvalidOperationException("DataGrid setup should expose the selected BindingSource with four fields.");
        if (!vm.SelectedGridColumnCompactSummary.Contains("4", StringComparison.Ordinal))
            throw new InvalidOperationException("DataGrid column summary should report four generated fields.");
    }

    private static void AssertPropertyGridDataGridSetup(SmokeContext context)
    {
        RequireContains(context.Xaml, "CustomersGrid", "PropertyGrid setup scenario should keep DataGrid in export.");
        RequireContains(context.ChecklistText, "Avalonia.Controls.DataGrid", "PropertyGrid setup should preserve required DataGrid package.");
        RequireNotContains(context.DiagnosticsText, "BindingSource не выбран", "PropertyGrid setup should keep DataGrid BindingSource assigned.");
    }

    private static void ConfigureSaveLoadMultiFormProject(MainWindowViewModel vm)
    {
        ConfigureAlphaProject(vm);

        var form2Document = vm.CurrentProject.Forms.First(form => string.Equals(form.DisplayName, "Form2", StringComparison.OrdinalIgnoreCase));
        SwitchToForm(vm, form2Document.Id);
        var form1Document = vm.CurrentProject.Forms.First(form => form.DisplayName.StartsWith("Form1", StringComparison.OrdinalIgnoreCase));
        SwitchToForm(vm, form1Document.Id);

        var json = JsonSerializer.Serialize(vm.Workspace, new JsonSerializerOptions { WriteIndented = true });
        var restored = JsonSerializer.Deserialize<WorkspaceModel>(json) ?? throw new InvalidOperationException("Workspace round-trip returned null.");
        if (restored.Project.Forms.Count != 2)
            throw new InvalidOperationException("Workspace round-trip should preserve two forms.");

        var form1 = restored.Project.Forms.FirstOrDefault(form => string.Equals(form.DisplayName, "Form1", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Workspace round-trip should preserve Form1.");
        var form2 = restored.Project.Forms.FirstOrDefault(form => string.Equals(form.DisplayName, "Form2", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Workspace round-trip should preserve Form2.");
        if (form1.Document.Controls.All(control => !string.Equals(control.Name, "CustomersGrid", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Workspace round-trip should preserve Form1 controls.");
        if (form1.Document.BindingSources.Count != 1 || form1.Document.BindingSources[0].Fields.Count != 4)
            throw new InvalidOperationException("Workspace round-trip should preserve BindingSource fields.");
        if (form1.Document.Interactions.Count != 4)
            throw new InvalidOperationException("Workspace round-trip should preserve Form1 interactions.");
        if (form2.Document.Controls.All(control => !string.Equals(control.Name, "Form2Title", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Workspace round-trip should preserve Form2 controls.");
    }

    private static void AssertSaveLoadMultiFormProject(SmokeContext context)
    {
        RequireGeneratedFile(context, "Form2.axaml");
        RequireContains(context.ChecklistText, "Forms exported: 2/2", "Save/load smoke should still export both forms.");
        RequireContains(context.CSharp, "new Form2()", "Save/load smoke should preserve OpenForm interaction.");
    }

    private static void ConfigureExportToProjectBuildValidation(MainWindowViewModel vm)
    {
        ConfigureAlphaProject(vm);
    }

    private static void AssertExportToProjectBuildValidation(SmokeContext context)
    {
        var scenarioRoot = Directory.GetParent(context.ProjectPath)?.FullName ?? context.ProjectPath;
        var validationRoot = Path.Combine(scenarioRoot, $"{context.Scenario.Name}-validation");
        var result = context.ViewModel.ValidateCurrentExportBuildAsync(validationRoot).GetAwaiter().GetResult();
        if (result.Status != ExportBuildValidationStatus.Passed)
            throw new InvalidOperationException($"Export pipeline build validation failed.{Environment.NewLine}{result.Output}");

        RequireContains(result.ProjectPath, "-validation", "Build validation should create a temporary validation project.");
        RequireContains(File.ReadAllText(Path.Combine(result.ProjectPath, "ExportValidation.csproj"), Encoding.UTF8), "<TargetFramework>net6.0</TargetFramework>", "Validation project should target net6.0.");
        RequireContains(File.ReadAllText(Path.Combine(result.ProjectPath, "ExportValidation.csproj"), Encoding.UTF8), "Avalonia.Controls.DataGrid", "Validation project should include DataGrid package.");
    }

    private static void ConfigureAlphaProject(MainWindowViewModel vm)
    {
        var source = CustomersSource();
        vm.BindingSources.Add(source);
        vm.SelectedBindingSource = source;
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;

        vm.Controls.Add(Control(DesignerControlTypes.TextBlock, "Form1Title", 30, 24, 360, 32, text: "Customers"));
        vm.Controls.Add(Control(DesignerControlTypes.Button, "OpenForm2Button", 30, 74, 190, 42, text: "Открыть Form2", background: "#2563EB", foreground: "#FFFFFF", border: "#1D4ED8", radius: 8));
        vm.Controls.Add(DataGrid("CustomersGrid", source.Id, 30, 136, 620, 280));
        vm.Controls.Add(Control(DesignerControlTypes.TextBox, "SelectedNameTextBox", 690, 136, 250, 38, placeholder: "Selected Name"));
        vm.Controls.Add(Control(DesignerControlTypes.CheckBox, "DetailsCheckBox", 690, 196, 180, 34, text: "Show details"));
        vm.Controls.Add(Control(DesignerControlTypes.Border, "DetailsPanel", 690, 250, 250, 118, background: "#EFF6FF", border: "#BFDBFE", radius: 10));

        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Active Form1 document missing.");
        form1.Name = "Form1";
        form1.Document.FormTitle = "Form1";
        vm.FormTitle = "Form1";
        vm.NewFormEditorCommand?.Execute(null);
        var form2 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Active Form2 document missing.");
        form2.Name = "Form2";
        form2.Document.FormTitle = "Form2";
        vm.FormTitle = "Form2";
        vm.Controls.Add(Control(DesignerControlTypes.TextBlock, "Form2Title", 40, 40, 360, 32, text: "Form2"));
        vm.Controls.Add(Control(DesignerControlTypes.TextBox, "Form2Input", 40, 96, 280, 40, placeholder: "Form2 input"));

        SwitchToForm(vm, form1.Id);
        RequireActiveControls(vm, "OpenForm2Button", "CustomersGrid", "SelectedNameTextBox", "DetailsCheckBox", "DetailsPanel");
        RequireNoActiveControls(vm, "Form2Title", "Form2Input");

        vm.Interactions.Add(new InteractionModel
        {
            SourceControlName = "OpenForm2Button",
            EventName = InteractionModel.EventButtonClick,
            ActionType = InteractionModel.ActionOpenForm,
            TargetFormId = form2.Id,
            TargetFormName = form2.DisplayName,
            OpenMode = InteractionModel.OpenModeShow
        });
        vm.Interactions.Add(new InteractionModel
        {
            SourceControlName = "CustomersGrid",
            EventName = InteractionModel.EventDataGridSelectionChanged,
            ActionType = InteractionModel.ActionSetProperty,
            TargetControlName = "SelectedNameTextBox",
            TargetProperty = InteractionModel.TargetPropertyText,
            SourcePath = "Name"
        });
        vm.Interactions.Add(new InteractionModel
        {
            SourceControlName = "DetailsCheckBox",
            EventName = InteractionModel.EventCheckBoxChecked,
            ActionType = InteractionModel.ActionToggleVisibility,
            TargetControlName = "DetailsPanel",
            TargetProperty = InteractionModel.TargetPropertyIsVisible
        });
        vm.Interactions.Add(new InteractionModel
        {
            SourceControlName = "DetailsCheckBox",
            EventName = InteractionModel.EventCheckBoxUnchecked,
            ActionType = InteractionModel.ActionToggleVisibility,
            TargetControlName = "DetailsPanel",
            TargetProperty = InteractionModel.TargetPropertyIsVisible
        });
    }

    private static void ConfigureTwoFormOpenFormProject(MainWindowViewModel vm)
    {
        vm.ExportTarget = MainWindowViewModel.ExportTargetMainWindow;
        vm.Controls.Add(Control(DesignerControlTypes.Button, "OpenForm2Button", 36, 92, 180, 42, text: "Open Form2", background: "#2563EB", foreground: "#FFFFFF", border: "#1D4ED8", radius: 10));
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Active Form1 document missing.");
        vm.NewFormEditorCommand?.Execute(null);
        var form2 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Active Form2 document missing.");
        form2.Name = "Form2";
        form2.Document.FormTitle = "Form2";
        vm.FormTitle = "Form2";
        vm.Controls.Add(Control(DesignerControlTypes.TextBlock, "Form2Title", 36, 34, 340, 30, text: "Form2 window"));
        SwitchToForm(vm, form1.Id);
        vm.Interactions.Add(new InteractionModel
        {
            SourceControlName = "OpenForm2Button",
            EventName = InteractionModel.EventButtonClick,
            ActionType = InteractionModel.ActionOpenForm,
            TargetFormId = form2.Id,
            TargetFormName = form2.DisplayName,
            OpenMode = InteractionModel.OpenModeShow
        });
    }

    private static BindingSourceModel CustomersSource()
    {
        var source = new BindingSourceModel
        {
            Id = "customers-source",
            Name = "CustomersSource",
            Path = "Customers",
            ItemTypeName = "CustomerRow",
            Description = "Alpha 0.2 customers source."
        };
        source.Fields.Add(Field("Id", "Id", "1", "int", "80"));
        source.Fields.Add(Field("Name", "Name", "Ada Lovelace", "string", "2*"));
        source.Fields.Add(Field("Email", "Email", "ada@example.com", "string", "2*"));
        source.Fields.Add(Field("Status", "Status", "Active", "string", "*"));
        return source;
    }

    private static BindingSourceModel SqlCustomersSource()
    {
        var source = CustomersSource();
        source.Id = "sql-customers-source";
        source.Name = "SqlCustomers";
        source.Path = "Customers";
        source.ItemTypeName = "CustomerRow";
        source.Description = "SQL customers source.";
        source.SourceKind = "SqlServer";
        source.SourceConnectionString = "Server=.;Database=Demo;User Id=designer;Password=secret;TrustServerCertificate=True";
        source.SourceSchemaName = "dbo";
        source.SourceTableName = "Customers";
        source.SourceQuery = "SELECT Id, Name, Email, Status FROM dbo.Customers";
        return source;
    }

    private static BindingSourceModel SqlRuntimeMismatchSource(string id, string path, string itemTypeName)
    {
        var source = new BindingSourceModel
        {
            Id = id,
            Name = "SqlRuntimeRows",
            Path = path,
            ItemTypeName = itemTypeName,
            Description = "SQL source with raw column names that require runtime DTO property sanitization.",
            SourceKind = "SqlServer",
            SourceConnectionString = "Server=.;Database=Demo;User Id=designer;Password=secret;TrustServerCertificate=True",
            SourceSchemaName = "dbo",
            SourceTableName = "RuntimeRows",
            SourceQuery = "SELECT [Кто выгрузил], [Рабочая станция], [Имя файла XML], [Id] FROM dbo.RuntimeRows"
        };
        source.Fields.Add(Field("Кто выгрузил", "Кто выгрузил", "operator", "string", "2*"));
        source.Fields.Add(Field("Рабочая станция", "Рабочая станция", "WS-01", "string", "2*"));
        source.Fields.Add(Field("Имя файла XML", "Имя файла XML", "sample.xml", "string", "2*"));
        source.Fields.Add(Field("Id", "Id", "1", "int", "80"));
        return source;
    }

    private static BindingSourceModel MultiFormSqlSource(
        string id,
        string path,
        string itemTypeName,
        string tableName,
        string query)
    {
        var source = new BindingSourceModel
        {
            Id = id,
            Name = $"{itemTypeName}Source",
            Path = path,
            ItemTypeName = itemTypeName,
            Description = "Multi-form SQL export source.",
            SourceKind = "SqlServer",
            SourceConnectionString = "",
            SourceSchemaName = "dbo",
            SourceTableName = tableName,
            SourceQuery = query,
            PreviewRowMode = BindingSourceModel.PreviewRowModeTopN,
            UseRealPreviewRowsIfAvailable = true,
            UseDemoData = false,
            AllowPreviewSampleFallback = false
        };
        source.Fields.Add(Field("Id", "Id", "1", "int", "90"));
        source.Fields.Add(Field("Name", "Name", "row", "string", "2*"));
        return source;
    }

    private static BindingSourceModel UnconfiguredSqlSource(bool allowDemoRows)
    {
        var source = new BindingSourceModel
        {
            Id = allowDemoRows ? "sql-unconfigured-demo-source" : "sql-unconfigured-empty-source",
            Name = "SqlSource",
            Path = "SqlSource",
            ItemTypeName = "SqlRow",
            Description = "SQL source selected in UI, but connection string, table and query are not configured.",
            SourceKind = "SqlServer",
            SourceConnectionString = "",
            SourceSchemaName = "dbo",
            SourceTableName = "",
            SourceQuery = "",
            PreviewRowMode = allowDemoRows ? BindingSourceModel.PreviewRowModeSampleRows : BindingSourceModel.PreviewRowModeSchemaOnly,
            PreviewTopN = 6,
            UseDemoData = allowDemoRows,
            AllowPreviewSampleFallback = false
        };
        source.Fields.Add(Field("Колонка 1", "Field1", "1", "int", "*"));
        source.Fields.Add(Field("Колонка 2", "Field2", "2", "int", "*"));
        source.Fields.Add(Field("Колонка 3", "Field3", "3", "int", "*"));
        return source;
    }

    private static BindingSourceModel DllOrdersSource()
    {
        var source = new BindingSourceModel
        {
            Id = "dll-orders-source",
            Name = "Orders",
            Path = "SalesOrders",
            ItemTypeName = "OrderRow",
            Description = "DLL table source.",
            SourceKind = "DllTable",
            SourceAssemblyPath = @"C:\DataSources\Sales.dll",
            SourceTypeFullName = "Contoso.Sales.Order",
            SourceTableName = "Orders"
        };
        source.Fields.Add(Field("OrderId", "OrderId", "1001", "int", "90"));
        source.Fields.Add(Field("CustomerName", "CustomerName", "Ada Lovelace", "string", "2*"));
        source.Fields.Add(Field("Status", "Status", "Open", "string", "*"));
        return source;
    }

    private static void SwitchToForm(MainWindowViewModel vm, string formId)
    {
        if (!vm.SetActiveForm(formId, "SmokeSwitchToForm"))
            throw new InvalidOperationException($"Could not switch to form {formId}.");

        if (!string.Equals(vm.ActiveDocumentId, formId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"ActiveDocumentId mismatch after switch. Expected {formId}, got {vm.ActiveDocumentId}.");
    }

    private static void RequirePropertyGridRow(MainWindowViewModel vm, string label)
    {
        var hasRow = vm.PropertyGridCategories
            .SelectMany(category => category.Rows)
            .Any(row => string.Equals(row.Label, label, StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.Key, label, StringComparison.OrdinalIgnoreCase));
        if (!hasRow)
            throw new InvalidOperationException($"PropertyGrid row missing: {label}");
    }

    private static void SetPropertyGridValue(MainWindowViewModel vm, string key, string value)
    {
        var row = GetPropertyGridRow(vm, key);
        row.Value = value;
        row.CommitValue();
    }

    private static void EditPropertyGridTextLikeUi(MainWindowViewModel vm, string key, string value)
    {
        var row = GetPropertyGridRow(vm, key);

        vm.BeginPropertyGridTextEdit(row, row.Value);
        var text = "";
        foreach (var character in value)
        {
            text += character;
            vm.UpdatePropertyGridTextEdit(row, text);
        }

        vm.CommitPropertyGridTextEdit(row, value);
    }

    private static PropertyGridRowViewModel GetPropertyGridRow(MainWindowViewModel vm, string key)
    {
        return vm.PropertyGridCategories
            .SelectMany(category => category.Rows)
            .FirstOrDefault(row => string.Equals(row.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"PropertyGrid row missing: {key}");
    }

    private static void RequirePropertyGridContext(MainWindowViewModel vm, string documentId, string controlId)
    {
        if (!string.Equals(vm.PropertyGridContextDocumentId, documentId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"PropertyGrid context document mismatch. Expected {documentId}, got {vm.PropertyGridContextDocumentId}.");

        if (!string.Equals(vm.PropertyGridContextControlId, controlId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"PropertyGrid context control mismatch. Expected {controlId}, got {vm.PropertyGridContextControlId}.");
    }

    private static void RequireInspectorSelection(MainWindowViewModel vm, string documentId, string controlId, string expectedTitlePrefix)
    {
        RequirePropertyGridContext(vm, documentId, controlId);
        RequirePropertyGridRowsContext(vm, documentId, controlId);

        if (vm.SelectedControl is null || !string.Equals(vm.SelectedControl.Id, controlId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SelectedControl does not match the expected inspector control.");

        if (!vm.PropertyGridSelectionTitle.StartsWith(expectedTitlePrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Inspector header mismatch. Expected prefix '{expectedTitlePrefix}', got '{vm.PropertyGridSelectionTitle}'.");
    }

    private static void RequirePropertyGridRowsContext(MainWindowViewModel vm, string documentId, string controlId)
    {
        var rows = vm.PropertyGridCategories.SelectMany(category => category.Rows).ToList();
        if (rows.Count == 0)
            throw new InvalidOperationException("PropertyGrid has no rows.");

        foreach (var row in rows.Where(row => !string.IsNullOrWhiteSpace(row.ContextControlId)))
        {
            if (!string.Equals(row.ContextDocumentId, documentId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(row.ContextControlId, controlId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"PropertyGrid row context mismatch for {row.Key}. Expected {documentId}/{controlId}, got {row.ContextDocumentId}/{row.ContextControlId}.");
            }
        }
    }

    private static void RequireNoDuplicatePropertyRows(MainWindowViewModel vm)
    {
        var duplicate = vm.PropertyGridCategories
            .SelectMany(category => category.Rows)
            .GroupBy(row => $"{row.ContextDocumentId}|{row.ContextControlId}|{row.Key}", StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            var rows = string.Join(", ", duplicate.Select(row => $"{row.Category}:{row.Label}"));
            throw new InvalidOperationException($"Duplicate PropertyGrid rows detected for {duplicate.Key}: {rows}");
        }
    }

    private static string CaptureEditorState(MainWindowViewModel vm)
    {
        return string.Join("|",
            vm.ActiveDocumentId,
            vm.SelectedControl?.Id ?? "",
            vm.PropertyGridContextDocumentId,
            vm.PropertyGridContextControlId,
            vm.RightInspectorSelectedIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            string.Join(",", vm.DocumentTabs.Select(tab => tab.DocumentId)),
            vm.CanvasRenderedDocumentId,
            vm.Controls.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            vm.PropertyGridCategories.Sum(category => category.Rows.Count).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void RequireEditorStateUnchanged(string before, string after, string message)
    {
        if (!string.Equals(before, after, StringComparison.Ordinal))
            throw new InvalidOperationException($"{message} Before: {before}. After: {after}.");
    }

    private static void RequireNoPreviewExportMismatches(PreviewExportComparisonResult comparison)
    {
        if (comparison.Mismatches.Count == 0)
            return;

        var details = string.Join(" | ", comparison.Mismatches.Select(item =>
            $"{item.Kind}:{item.ControlId}:preview={item.PreviewValue}:export={item.ExportValue}:{item.Details}"));
        throw new InvalidOperationException($"Preview/export mismatch detected: {details}");
    }

    private static void RequireControlOrder(IReadOnlyList<PreviewExportControlSnapshot> snapshots, params string[] expectedNames)
    {
        var actualNames = snapshots.Select(snapshot => snapshot.Name).ToList();
        for (var index = 0; index < expectedNames.Length; index++)
        {
            if (index >= actualNames.Count || !string.Equals(actualNames[index], expectedNames[index], StringComparison.Ordinal))
                throw new InvalidOperationException($"Unexpected control order. Expected: {string.Join(", ", expectedNames)}. Actual: {string.Join(", ", actualNames)}.");
        }
    }

    private static void RequireSameBounds(PreviewExportControlSnapshot preview, PreviewExportControlSnapshot export)
    {
        const double tolerance = 0.01;
        if (Math.Abs(preview.X - export.X) > tolerance
            || Math.Abs(preview.Y - export.Y) > tolerance
            || Math.Abs(preview.Width - export.Width) > tolerance
            || Math.Abs(preview.Height - export.Height) > tolerance)
        {
            throw new InvalidOperationException($"Bounds mismatch for {preview.Name}. Preview: {preview.BoundsText}. Export: {export.BoundsText}.");
        }
    }

    private static Dictionary<string, string> CaptureBoundsByName(MainWindowViewModel vm)
    {
        return vm.Controls.ToDictionary(
            control => control.Name,
            control => $"{control.X.ToString(System.Globalization.CultureInfo.InvariantCulture)},{control.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)} {control.Width.ToString(System.Globalization.CultureInfo.InvariantCulture)}x{control.Height.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            StringComparer.OrdinalIgnoreCase);
    }

    private static void RequireBoundsMapUnchanged(Dictionary<string, string> before, Dictionary<string, string> after, string message)
    {
        foreach (var (name, beforeBounds) in before)
        {
            if (!after.TryGetValue(name, out var afterBounds))
                throw new InvalidOperationException($"{message} Missing control after resize: {name}.");
            if (!string.Equals(beforeBounds, afterBounds, StringComparison.Ordinal))
                throw new InvalidOperationException($"{message} {name}: before={beforeBounds}; after={afterBounds}.");
        }
    }

    private static void RequireNoApplyDocumentTrace(MainWindowViewModel vm, string scenario)
    {
        var applyTrace = vm.InteractionTraceEntries
            .Where(entry => entry.EventName.Contains("APPLY_DOCUMENT", StringComparison.OrdinalIgnoreCase)
                || entry.EventName == "ApplyDocument")
            .Select(entry => $"{entry.EventName}:{entry.Details}")
            .ToList();
        if (applyTrace.Count > 0)
            throw new InvalidOperationException($"{scenario}: export pipeline called ApplyDocument: {string.Join(" | ", applyTrace)}");
    }

    private static void RequireNoExportEditorMutationTrace(MainWindowViewModel vm)
    {
        var mutations = vm.InteractionTraceEntries
            .Where(entry => entry.EventName == "EXPORT_MUTATED_EDITOR_STATE"
                || entry.EventName == "EDITOR_STATE_MUTATION_DURING_EXPORT_BLOCKED")
            .Select(entry => $"{entry.EventName}:{entry.Details}")
            .ToList();
        if (mutations.Count > 0)
            throw new InvalidOperationException($"Export mutated editor state: {string.Join(" | ", mutations)}");
    }

    private static void RequireActiveControls(MainWindowViewModel vm, params string[] names)
    {
        foreach (var name in names)
        {
            if (!vm.Controls.Any(control => string.Equals(control.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Expected active form to contain control '{name}'.");
        }
    }

    private static void RequireNoActiveControls(MainWindowViewModel vm, params string[] names)
    {
        foreach (var name in names)
        {
            if (vm.Controls.Any(control => string.Equals(control.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Phantom control leaked into active form: '{name}'.");
        }
    }

    private static void ConfigurePluginFallbackExport(MainWindowViewModel vm)
    {
        vm.IncludePluginRuntimeReferences = false;
        vm.Controls.Add(Control("Minimal.HelloCard", "HelloCard1", 42, 42, 280, 118, text: "Hello plugin", pluginId: "Samples.MinimalDesignerPlugin", pluginVersion: "1.0.0"));
    }

    private static void AssertPluginFallbackExport(SmokeContext context)
    {
        RequireContains(context.Xaml, "Plugin control 'Minimal.HelloCard'", "Plugin placeholder comment missing.");
        RequireContains(context.Xaml, "<Border", "Plugin fallback should export a safe Border placeholder.");
        RequireNotContains(context.Xaml, "minimal:HelloCard", "Clean fallback export must not require plugin runtime namespace.");
        RequireContains(context.DiagnosticsText, "placeholder", "Diagnostics should warn about plugin placeholder export.");
        RequireContains(context.ChecklistText, "Plugins: none", "Checklist should report no runtime plugin DLLs in fallback mode.");
    }

    private static void ConfigureEremexTextEditorVerticalSlice(MainWindowViewModel vm)
    {
        LoadBuiltEremexPlugin(vm);
        vm.IncludePluginRuntimeReferences = false;

        var form = vm.ActiveFormDocument ?? throw new InvalidOperationException("Eremex smoke form is missing.");
        var editor = vm.TryCreateControlFromToolboxDrop("Eremex.TextEditor", 64, 72, null, false, form.Id)
            ?? throw new InvalidOperationException("Eremex TextEditor was not created from Toolbox.");
        editor.Name = "EremexTextEditor1";
        editor.Width = 280;
        editor.Height = 40;
        SetCustomProperty(editor, "EditorValue", "Ada Lovelace");
        SetCustomProperty(editor, "Watermark", "Введите имя");
        SetCustomProperty(editor, "ReadOnly", false);
        vm.SelectSingleControl(editor);
    }

    private static void AssertEremexThemeDoesNotChangeDesignerChrome(SmokeContext context)
    {
        EnsureAvaloniaRuntimeInitialized();
        var application = Application.Current
            ?? throw new InvalidOperationException("Avalonia application was not initialized for the Eremex theme scope test.");
        var globalStyleCount = application.Styles.Count;
        var editor = context.ViewModel.Controls.Single(control => control.Type == "Eremex.TextEditor");
        var canvasWindow = new MainWindow { DataContext = context.ViewModel };
        var preview = InvokePreviewFactory(canvasWindow, "CreatePreviewControl", editor);

        if (application.Styles.Count != globalStyleCount)
        {
            throw new InvalidOperationException(
                "DeltaDesignTheme changed Application.Current.Styles while creating an Eremex Canvas preview.");
        }

        if (application.Styles.Any(style => style.GetType().FullName?.Contains("Eremex", StringComparison.OrdinalIgnoreCase) == true))
            throw new InvalidOperationException("Designer chrome must not receive Eremex styles through Application.Current.Styles.");
        if (!preview.Styles.Any(style => style.GetType().FullName?.Contains("EremexPreviewThemeResource", StringComparison.Ordinal) == true))
            throw new InvalidOperationException("Eremex Canvas preview must receive DeltaDesignTheme through its local style scope.");
    }

    private static void ConfigureEremexDataGridControlVerticalSlice(MainWindowViewModel vm)
    {
        LoadBuiltEremexPlugin(vm);
        vm.IncludePluginRuntimeReferences = false;
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;

        var source = ProductsSource();
        source.UseDemoData = true;
        vm.BindingSources.Add(source);

        var form = vm.ActiveFormDocument ?? throw new InvalidOperationException("Eremex DataGrid smoke form is missing.");
        var grid = vm.TryCreateControlFromToolboxDrop("Eremex.DataGridControl", 64, 72, null, false, form.Id)
            ?? throw new InvalidOperationException("Eremex DataGridControl was not created from Toolbox.");
        grid.Name = "EremexDataGrid1";
        grid.Width = 700;
        grid.Height = 380;
        grid.BindingSourceId = source.Id;
        grid.AutoGenerateColumns = true;
        SetCustomProperty(grid, "ShowGroupPanel", true);
        SetCustomProperty(grid, "ShowAutoFilterRow", true);
        SetCustomProperty(grid, "AllowSorting", true);
        SetCustomProperty(grid, "RowMinHeight", 30d);

        var emptyGrid = vm.TryCreateControlFromToolboxDrop("Eremex.DataGridControl", 64, 486, null, false, form.Id)
            ?? throw new InvalidOperationException("Empty Eremex DataGridControl was not created from Toolbox.");
        emptyGrid.Name = "EremexDataGridEmpty";
        emptyGrid.Width = 700;
        emptyGrid.Height = 180;
        emptyGrid.BindingSourceId = "";
        emptyGrid.AutoGenerateColumns = true;

        var manualColumnsGrid = vm.TryCreateControlFromToolboxDrop("Eremex.DataGridControl", 792, 72, null, false, form.Id)
            ?? throw new InvalidOperationException("Manual-column Eremex DataGridControl was not created from Toolbox.");
        manualColumnsGrid.Name = "EremexDataGridManualColumns";
        manualColumnsGrid.Width = 420;
        manualColumnsGrid.Height = 260;
        manualColumnsGrid.BindingSourceId = source.Id;
        manualColumnsGrid.AutoGenerateColumns = false;
        vm.SelectSingleControl(grid);
    }

    private static void AssertEremexDataGridControlVerticalSlice(SmokeContext context)
    {
        var vm = context.ViewModel;
        if (!vm.Registry.TryGetControl("Eremex.DataGridControl", out var descriptor))
            throw new InvalidOperationException("Eremex DataGridControl descriptor was not registered by PluginLoader.");
        if (descriptor is not IDesignerControlProviderMetadata metadata
            || !string.Equals(metadata.ProviderId, "Eremex", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(metadata.ToolboxGroup, "Eremex", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Eremex DataGridControl must declare Eremex Toolbox metadata.");
        }

        var eremexGroup = vm.ToolboxGroups.FirstOrDefault(group =>
            string.Equals(group.ProviderId, "Eremex", StringComparison.OrdinalIgnoreCase));
        if (eremexGroup?.Items.Any(item => item.Type == "Eremex.DataGridControl") != true)
            throw new InvalidOperationException("Eremex DataGridControl must appear in the Eremex Toolbox group.");
        if (vm.ToolboxGroups.FirstOrDefault(group => string.Equals(group.ProviderId, "Avalonia", StringComparison.OrdinalIgnoreCase))?.Items
                .Any(item => item.Type == DesignerControlTypes.DataGrid) != true)
        {
            throw new InvalidOperationException("Standard Avalonia DataGrid must remain in the Standard Toolbox group.");
        }

        var grid = vm.Controls.Single(control => control.Type == "Eremex.DataGridControl" && control.Name == "EremexDataGrid1");
        var emptyGrid = vm.Controls.Single(control => control.Type == "Eremex.DataGridControl" && control.Name == "EremexDataGridEmpty");
        var manualColumnsGrid = vm.Controls.Single(control => control.Type == "Eremex.DataGridControl" && control.Name == "EremexDataGridManualColumns");
        foreach (var expectedKey in new[] { "ShowGroupPanel", "ShowAutoFilterRow", "AllowSorting", "RowMinHeight", "Eremex.ClrType", "Eremex.PackageId" })
        {
            if (!grid.CustomProperties.Any(property => string.Equals(property.Key, expectedKey, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Eremex DataGridControl is missing persisted custom property '{expectedKey}'.");
        }

        foreach (var expectedKey in new[]
                 {
                     "ShowGroupPanel", "ShowAutoFilterRow", "AllowSorting", "AllowEditing", "AllowColumnResizing",
                     "AllowColumnMoving", "IsSearchPanelVisible", "ShowSearchPanelCloseButton", "AutoExpandAllGroups",
                     "RowMinHeight", "HeaderPanelMinHeight", "NavigationMode", "EditorShowMode"
                 })
        {
            if (!vm.DescriptorCustomPropertyEditors.Any(property => string.Equals(property.Key, expectedKey, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Property Inspector is missing Eremex DataGrid property '{expectedKey}'.");
        }
        if (!vm.PropertyGridCategories.SelectMany(category => category.Rows).Any(row => row.Key == nameof(DesignControlModel.BindingSourceId))
            || !vm.PropertyGridCategories.SelectMany(category => category.Rows).Any(row => row.Key == nameof(DesignControlModel.AutoGenerateColumns)))
        {
            throw new InvalidOperationException("Eremex DataGrid must expose BindingSource and AutoGenerateColumns in Property Inspector.");
        }
        var visiblePropertyKeys = vm.PropertyGridCategories.SelectMany(category => category.Rows)
            .Select(row => row.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var internalKey in new[] { "Eremex.ClrType", "Eremex.PackageId", "Eremex.PackageVersion", "Eremex.ThemePackageId" })
        {
            if (visiblePropertyKeys.Contains(internalKey))
                throw new InvalidOperationException($"Internal Eremex dependency metadata '{internalKey}' must not be editable in Property Inspector.");
        }

        EnsureAvaloniaRuntimeInitialized();
        var canvasWindow = new MainWindow { DataContext = vm };
        var canvasGrid = InvokePreviewFactory(canvasWindow, "CreatePreviewControl", grid);
        AssertRealEremexDataGrid(canvasGrid, "Designer Canvas");
        RequireControlCanApplyTemplateAndLayout(canvasGrid, "Eremex DataGrid Designer Canvas");
        if (GetItemsSourceCount(canvasGrid) == 0)
            throw new InvalidOperationException("Eremex DataGrid Canvas preview did not receive explicit demo rows from BindingSource infrastructure.");
        RequireEremexDataGridColumns(canvasGrid, new[] { "Title", "Price", "Count" }, "Eremex DataGrid Canvas schema");
        RequireEremexDataGridVisualParts(canvasGrid, "Eremex DataGrid Designer Canvas");

        var emptyCanvasGrid = InvokePreviewFactory(canvasWindow, "CreatePreviewControl", emptyGrid);
        AssertRealEremexDataGrid(emptyCanvasGrid, "Designer Canvas empty grid");
        RequireControlCanApplyTemplateAndLayout(emptyCanvasGrid, "Eremex DataGrid empty Canvas");
        if (GetItemsSourceCount(emptyCanvasGrid) != 0)
            throw new InvalidOperationException("Eremex DataGrid without a source must remain empty and must not generate fake rows.");
        if (GetEremexDataGridColumnCount(emptyCanvasGrid) != 0)
            throw new InvalidOperationException("Eremex DataGrid without a source must not generate fake Field1...FieldN columns.");

        var manualCanvasGrid = InvokePreviewFactory(canvasWindow, "CreatePreviewControl", manualColumnsGrid);
        AssertRealEremexDataGrid(manualCanvasGrid, "Designer Canvas manual columns");
        RequireControlCanApplyTemplateAndLayout(manualCanvasGrid, "Eremex DataGrid manual columns Canvas");
        RequireEremexDataGridColumns(manualCanvasGrid, new[] { "Title", "Price", "Count" }, "Eremex DataGrid manual schema");

        var previewDocument = vm.CreatePreviewDocumentSnapshot();
        var legacyWindow = new PreviewWindow(previewDocument, vm.Registry);
        var legacyModel = previewDocument.Controls.Single(control => control.Name == "EremexDataGrid1");
        var legacyGrid = InvokePreviewFactory(legacyWindow, "CreatePreviewControl", legacyModel);
        AssertRealEremexDataGrid(legacyGrid, "Legacy Preview Window");
        RequireControlCanApplyTemplateAndLayout(legacyGrid, "Eremex DataGrid Legacy Preview");

        RequireContains(context.Xaml, "xmlns:mxdg=\"https://schemas.eremexcontrols.net/avalonia/datagrid\"", "Eremex DataGrid XML namespace was not generated.");
        RequireContains(context.Xaml, "<mxdg:DataGridControl", "Eremex DataGridControl AXAML tag was not generated.");
        RequireContains(context.Xaml, "AutoGenerateColumns=\"True\"", "Eremex DataGridControl must export AutoGenerateColumns.");
        RequireContains(context.Xaml, "<mxdg:DataGridControl.Columns>", "Eremex manual columns must use the documented Eremex property-element syntax.");
        RequireContains(context.Xaml, "<mxdg:GridColumn FieldName=\"Title\" Header=\"Title\"", "Eremex manual column must export its real FieldName and Header.");
        RequireContains(context.Xaml, "ItemsSource=\"{Binding ProductsView}\"", "Eremex DataGridControl must use the generated runtime ItemsSource path.");
        var emptyGridStart = context.Xaml.IndexOf("x:Name=\"EremexDataGridEmpty\"", StringComparison.Ordinal);
        var emptyGridEnd = emptyGridStart < 0 ? -1 : context.Xaml.IndexOf("/>", emptyGridStart, StringComparison.Ordinal);
        if (emptyGridStart < 0 || emptyGridEnd < 0)
            throw new InvalidOperationException("The unbound Eremex DataGrid AXAML element was not generated as a self-closing element.");
        RequireNotContains(context.Xaml.Substring(emptyGridStart, emptyGridEnd - emptyGridStart), "ItemsSource=", "Unbound Eremex DataGrid must not receive a synthetic ItemsSource.");
        RequireContains(context.CSharp, "SeedProductRow", "Explicit demo data must generate rows through the shared runtime binding pipeline.");

        var packages = vm.RequiredPackages.ToDictionary(package => package.Id, package => package.Version, StringComparer.OrdinalIgnoreCase);
        if (!packages.TryGetValue("Eremex.Avalonia.Controls", out var controlsVersion) || controlsVersion != "1.0.98"
            || !packages.TryGetValue("Eremex.Avalonia.Themes.DeltaDesign", out var themeVersion) || themeVersion != "1.0.98")
        {
            throw new InvalidOperationException("Eremex DataGrid export must include Controls and DeltaDesign packages 1.0.98.");
        }

        var savedWorkspace = vm.ExportWorkspaceJson();
        var reloaded = CreateViewModel("EremexDataGridReloaded");
        LoadBuiltEremexPlugin(reloaded);
        reloaded.LoadDocumentJson(savedWorkspace);
        if (reloaded.Controls.Count(control => control.Type == "Eremex.DataGridControl") != 3)
            throw new InvalidOperationException("Eremex DataGridControl models were not restored from project JSON.");

        var withoutPlugin = CreateViewModel("EremexDataGridMissingPlugin");
        withoutPlugin.LoadDocumentJson(savedWorkspace);
        if (withoutPlugin.Controls.Count(control => control.Type == "Eremex.DataGridControl") != 3
            || withoutPlugin.Controls.Any(control => control.Type == "Eremex.DataGridControl"
                                                     && !control.CustomProperties.Any(property => property.Key == "ShowGroupPanel")))
        {
            throw new InvalidOperationException("Missing Eremex plugin must preserve DataGridControl models and custom properties.");
        }
        withoutPlugin.GenerateXaml();
        RequireContains(withoutPlugin.GeneratedXaml, "Placeholder плагина: Eremex.DataGridControl", "Missing Eremex DataGrid plugin must export a safe placeholder.");

        using var exportedProject = ExportToTemporaryProject(context);
        var exportFolder = exportedProject.Path;
        var appAxaml = File.ReadAllText(Path.Combine(exportFolder, "App.axaml"), Encoding.UTF8);
        var projectFile = File.ReadAllText(Directory.GetFiles(exportFolder, "*.csproj").Single(), Encoding.UTF8);
        RequireContains(appAxaml, "<theme:DeltaDesignTheme />", "Generated App.axaml must load DeltaDesignTheme for Eremex DataGridControl.");
        RequireContains(projectFile, "Eremex.Avalonia.Controls\" Version=\"1.0.98", "Generated Eremex DataGrid project is missing the controls package.");

        var axamlPreview = vm.GenerateExportedAxamlPreviewSnapshot();
        var axamlRoot = RuntimeAxamlPreviewLoader.Load(
                            axamlPreview.Axaml,
                            axamlPreview.RuntimePreviewContributions.SelectMany(contribution => contribution.Assemblies)) as Control
            ?? throw new InvalidOperationException("Runtime AXAML Preview did not return a Control root for Eremex DataGridControl.");
        foreach (var contribution in axamlPreview.RuntimePreviewContributions)
            contribution.ApplyToPreviewRoot?.Invoke(axamlRoot);
        var axamlGrid = axamlRoot.GetLogicalDescendants()
            .OfType<Control>()
            .FirstOrDefault(item => string.Equals(item.GetType().FullName, "Eremex.AvaloniaUI.Controls.DataGrid.DataGridControl", StringComparison.Ordinal));
        if (axamlGrid is null)
            throw new InvalidOperationException("Runtime AXAML Preview does not contain the actual Eremex DataGridControl.");
        AssertRealEremexDataGrid(axamlGrid, "Runtime AXAML Preview");
        RequireControlCanApplyTemplateAndLayout(axamlRoot, "Eremex DataGrid Runtime AXAML Preview");

        var axamlPreviewWindow = new PreviewWindow(axamlPreview, vm.Registry);
        InvokeAsyncPreviewLoader(axamlPreviewWindow, "LoadExportedAxamlPreviewAsync", axamlPreview);
        var previewStatus = axamlPreviewWindow.FindControl<TextBlock>("PreviewRuntimeStatusText")?.Text;
        if (!string.Equals(previewStatus, "AXAML Preview ready", StringComparison.Ordinal))
            throw new InvalidOperationException($"AXAML Preview host did not load Eremex DataGridControl: status='{previewStatus ?? "-"}'.");
        var previewSurface = axamlPreviewWindow.FindControl<Border>("PreviewSurfaceBorder")
            ?? throw new InvalidOperationException("AXAML Preview surface was not found.");
        var hostedGrid = previewSurface.GetLogicalDescendants()
            .OfType<Control>()
            .FirstOrDefault(item => string.Equals(item.GetType().FullName, "Eremex.AvaloniaUI.Controls.DataGrid.DataGridControl", StringComparison.Ordinal));
        if (hostedGrid is null)
            throw new InvalidOperationException("AXAML Preview Window did not host the actual Eremex DataGridControl.");
        AssertRealEremexDataGrid(hostedGrid, "AXAML Preview Window");
        if (GetItemsSourceCount(hostedGrid) == 0)
            throw new InvalidOperationException("AXAML Preview Window did not assign runtime preview rows to Eremex DataGridControl.");
        RequireControlCanApplyTemplateAndLayout(axamlPreviewWindow, "Eremex DataGrid AXAML Preview Window");
        RequireGeneratedEremexDataGridApplicationRenders(context, exportFolder);
    }

    private static void AssertEremexTextEditorVerticalSlice(SmokeContext context)
    {
        var vm = context.ViewModel;
        if (!vm.Registry.TryGetControl("Eremex.TextEditor", out var descriptor))
            throw new InvalidOperationException("Eremex TextEditor descriptor was not registered by PluginLoader.");
        if (AssemblyLoadContext.Default.Assemblies.All(assembly =>
                !string.Equals(assembly.GetName().Name, "Eremex.Avalonia.Controls", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "The Eremex runtime manifest did not preload Eremex.Avalonia.Controls into the Default AssemblyLoadContext.");
        }

        if (descriptor is not IDesignerControlProviderMetadata metadata
            || !string.Equals(metadata.ProviderId, "Eremex", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(metadata.ToolboxGroup, "Eremex", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Eremex TextEditor descriptor must declare provider metadata for the Toolbox.");
        }

        var standardGroup = vm.ToolboxGroups.FirstOrDefault(group =>
            string.Equals(group.ProviderId, "Avalonia", StringComparison.OrdinalIgnoreCase));
        var eremexGroup = vm.ToolboxGroups.FirstOrDefault(group =>
            string.Equals(group.ProviderId, "Eremex", StringComparison.OrdinalIgnoreCase));
        if (standardGroup?.Items.Any(item => item.Type == DesignerControlTypes.Button) != true)
            throw new InvalidOperationException("Standard Avalonia controls must stay in the Standard Toolbox group.");
        if (eremexGroup?.Items.Any(item => item.Type == "Eremex.TextEditor") != true)
            throw new InvalidOperationException("Eremex TextEditor must appear in the Eremex Toolbox group.");
        if (vm.ToolboxItems.Count(item => item.Type == "Eremex.TextEditor") != 1)
            throw new InvalidOperationException("Eremex TextEditor must not be duplicated in Toolbox collections.");

        var editor = vm.Controls.Single(control => control.Type == "Eremex.TextEditor");
        if (!string.Equals(editor.PluginId, "Eremex.DesignerPlugin", StringComparison.Ordinal)
            || !string.Equals(editor.PluginVersion, "1.0.98", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Eremex TextEditor must persist plugin identity and package-line version.");
        }

        var customKeys = editor.CustomProperties.Select(property => property.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var expectedKey in new[] { "EditorValue", "Watermark", "ReadOnly", "Mask", "Eremex.ClrType", "Eremex.PackageId" })
        {
            if (!customKeys.Contains(expectedKey))
                throw new InvalidOperationException($"Eremex TextEditor is missing persisted custom property '{expectedKey}'.");
        }

        foreach (var expectedKey in new[] { "EditorValue", "Watermark", "ReadOnly", "Mask" })
        {
            if (!vm.DescriptorCustomPropertyEditors.Any(property => string.Equals(property.Key, expectedKey, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Property Inspector is missing Eremex property '{expectedKey}'.");
        }

        var maskTypeEditor = vm.DescriptorCustomPropertyEditors.SingleOrDefault(property =>
            string.Equals(property.Key, "MaskType", StringComparison.OrdinalIgnoreCase));
        if (maskTypeEditor is null
            || !maskTypeEditor.IsEnumEditor
            || maskTypeEditor.Options.Count == 0)
        {
            throw new InvalidOperationException("Eremex enum properties must use the existing inline ComboBox editor.");
        }

        var editorValueRow = vm.DescriptorCustomPropertyEditors.Single(property =>
            string.Equals(property.Key, "EditorValue", StringComparison.OrdinalIgnoreCase));
        RequireContains(editorValueRow.Descriptor.Description, "Текстовое значение", "Eremex descriptor property descriptions must reach the Property Inspector.");

        var savedWorkspace = vm.ExportWorkspaceJson();
        RequireContains(savedWorkspace, "Eremex.TextEditor", "Eremex control type must be persisted in project JSON.");
        RequireContains(savedWorkspace, "Eremex.DesignerPlugin", "Eremex plugin id must be persisted in project JSON.");

        var reloaded = CreateViewModel("EremexTextEditorReloaded");
        LoadBuiltEremexPlugin(reloaded);
        reloaded.LoadDocumentJson(savedWorkspace);
        var reloadedEditor = reloaded.Controls.SingleOrDefault(control => control.Type == "Eremex.TextEditor")
            ?? throw new InvalidOperationException("Eremex TextEditor was lost after project reload.");
        if (!string.Equals(reloadedEditor.PluginVersion, "1.0.98", StringComparison.Ordinal)
            || !reloadedEditor.CustomProperties.Any(property => string.Equals(property.Key, "Watermark", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Eremex TextEditor custom properties were not restored from project JSON.");
        }

        var withoutPlugin = CreateViewModel("EremexTextEditorMissingPlugin");
        withoutPlugin.LoadDocumentJson(savedWorkspace);
        var missingEditor = withoutPlugin.Controls.SingleOrDefault(control => control.Type == "Eremex.TextEditor")
            ?? throw new InvalidOperationException("Missing Eremex plugin must not discard the stored control model.");
        if (!missingEditor.CustomProperties.Any(property => string.Equals(property.Key, "EditorValue", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Missing Eremex plugin must preserve original custom properties.");
        withoutPlugin.GenerateXaml();
        RequireContains(withoutPlugin.GeneratedXaml, "Placeholder плагина: Eremex.TextEditor", "Missing Eremex plugin must export a safe placeholder without losing the model.");

        EnsureAvaloniaRuntimeInitialized();
        Control preview;
        try
        {
            preview = descriptor.BuildPreview(CreateSmokeControlNode(editor), new SmokePreviewContext());
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Eremex TextEditor legacy preview failed: {ex}", ex);
        }
        if (!string.Equals(preview.GetType().FullName, "Eremex.AvaloniaUI.Controls.Editors.TextEditor", StringComparison.Ordinal))
            throw new InvalidOperationException($"Legacy Preview must create the real Eremex TextEditor, got '{preview.GetType().FullName}'.");
        RequireControlCanApplyTemplateAndLayout(preview, "Descriptor preview");
        RequireSingleCoreAvaloniaAssemblyIdentity();

        var canvasWindow = new MainWindow { DataContext = vm };
        var canvasPreview = InvokePreviewFactory(canvasWindow, "CreatePreviewControl", editor);
        AssertRealEremexTextEditor(canvasPreview, "Designer Canvas");
        RequireControlCanApplyTemplateAndLayout(canvasPreview, "Designer Canvas");

        var previewDocument = vm.CreatePreviewDocumentSnapshot();
        var legacyWindow = new PreviewWindow(previewDocument, vm.Registry);
        var legacyModel = previewDocument.Controls.Single(control => control.Type == "Eremex.TextEditor");
        var legacyPreview = InvokePreviewFactory(legacyWindow, "CreatePreviewControl", legacyModel);
        AssertRealEremexTextEditor(legacyPreview, "Legacy Preview Window");
        RequireControlCanApplyTemplateAndLayout(legacyPreview, "Legacy Preview Window");

        RequireContains(context.Xaml, "xmlns:mxe=\"https://schemas.eremexcontrols.net/avalonia/editors\"", "Eremex XML namespace was not generated.");
        RequireContains(context.Xaml, "<mxe:TextEditor", "Eremex TextEditor AXAML tag was not generated.");
        RequireContains(context.Xaml, "EditorValue=\"Ada Lovelace\"", "Eremex EditorValue was not exported.");
        RequireContains(context.Xaml, "Watermark=\"Введите имя\"", "Eremex Watermark was not exported.");

        var packages = vm.RequiredPackages.ToDictionary(package => package.Id, package => package.Version, StringComparer.OrdinalIgnoreCase);
        if (!packages.TryGetValue("Eremex.Avalonia.Controls", out var controlsVersion) || controlsVersion != "1.0.98"
            || !packages.TryGetValue("Eremex.Avalonia.Themes.DeltaDesign", out var themeVersion) || themeVersion != "1.0.98")
        {
            throw new InvalidOperationException("Eremex export contribution must add controls and DeltaDesign packages 1.0.98.");
        }

        using var exportedProject = ExportToTemporaryProject(context);
        var exportFolder = exportedProject.Path;
        var appAxaml = File.ReadAllText(Path.Combine(exportFolder, "App.axaml"), Encoding.UTF8);
        var projectFile = File.ReadAllText(Directory.GetFiles(exportFolder, "*.csproj").Single(), Encoding.UTF8);
        var readme = File.ReadAllText(Path.Combine(exportFolder, "README.generated.md"), Encoding.UTF8);
        RequireContains(appAxaml, "xmlns:theme=\"clr-namespace:Eremex.AvaloniaUI.Themes.DeltaDesign;assembly=Eremex.Avalonia.Themes.DeltaDesign\"", "Generated App.axaml must declare the Eremex theme namespace.");
        RequireContains(appAxaml, "<theme:DeltaDesignTheme />", "Generated App.axaml must load DeltaDesignTheme.");
        RequireContains(projectFile, "Eremex.Avalonia.Controls\" Version=\"1.0.98", "Generated project is missing the Eremex controls package.");
        RequireContains(projectFile, "Eremex.Avalonia.Themes.DeltaDesign\" Version=\"1.0.98", "Generated project is missing the Eremex theme package.");
        RequireContains(readme, "Bring Your Own Package / Bring Your Own License", "Generated README must explain the Eremex licensing model.");

        var axamlPreview = vm.GenerateExportedAxamlPreviewSnapshot();
        var axamlRoot = RuntimeAxamlPreviewLoader.Load(
                            axamlPreview.Axaml,
                            axamlPreview.RuntimePreviewContributions.SelectMany(contribution => contribution.Assemblies)) as Control
            ?? throw new InvalidOperationException("Runtime AXAML Preview did not return a Control root.");
        foreach (var contribution in axamlPreview.RuntimePreviewContributions)
            contribution.ApplyToPreviewRoot?.Invoke(axamlRoot);
        var axamlEditor = axamlRoot.GetLogicalDescendants()
            .FirstOrDefault(item => string.Equals(item.GetType().FullName, "Eremex.AvaloniaUI.Controls.Editors.TextEditor", StringComparison.Ordinal)) as Control;
        if (axamlEditor is null)
            throw new InvalidOperationException("Runtime AXAML Preview does not contain the actual Eremex TextEditor.");
        AssertRealEremexTextEditor(axamlEditor, "Runtime AXAML Preview");
        RequireControlCanApplyTemplateAndLayout(axamlRoot, "Runtime AXAML Preview");

        var axamlPreviewWindow = new PreviewWindow(axamlPreview, vm.Registry);
        InvokeAsyncPreviewLoader(axamlPreviewWindow, "LoadExportedAxamlPreviewAsync", axamlPreview);
        var previewStatus = axamlPreviewWindow.FindControl<TextBlock>("PreviewRuntimeStatusText")?.Text;
        if (!string.Equals(previewStatus, "AXAML Preview ready", StringComparison.Ordinal))
            throw new InvalidOperationException($"AXAML Preview host did not load Eremex TextEditor: status='{previewStatus ?? "-"}'.");
        var previewSurface = axamlPreviewWindow.FindControl<Border>("PreviewSurfaceBorder")
            ?? throw new InvalidOperationException("AXAML Preview surface was not found.");
        var hostedEditor = previewSurface.GetLogicalDescendants()
            .OfType<Control>()
            .FirstOrDefault(item => string.Equals(item.GetType().FullName, "Eremex.AvaloniaUI.Controls.Editors.TextEditor", StringComparison.Ordinal));
        if (hostedEditor is null)
            throw new InvalidOperationException("AXAML Preview Window did not host the actual Eremex TextEditor.");
        AssertRealEremexTextEditor(hostedEditor, "AXAML Preview Window");
        RequireControlCanApplyTemplateAndLayout(axamlPreviewWindow, "AXAML Preview Window");
        RequireGeneratedEremexApplicationRenders(context, exportFolder);
    }

    private static Control InvokePreviewFactory(object host, string methodName, object model)
    {
        var method = host.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Preview factory '{host.GetType().Name}.{methodName}' was not found.");
        try
        {
            return method.Invoke(host, new[] { model }) as Control
                ?? throw new InvalidOperationException($"Preview factory '{host.GetType().Name}.{methodName}' returned no Control.");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw new InvalidOperationException($"Preview factory '{host.GetType().Name}.{methodName}' failed: {ex.InnerException}", ex.InnerException);
        }
    }

    private static void AssertRealEremexTextEditor(Control preview, string path)
    {
        if (!string.Equals(preview.GetType().FullName, "Eremex.AvaloniaUI.Controls.Editors.TextEditor", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{path} must render the actual Eremex TextEditor, got '{preview.GetType().FullName}'.");
        }
    }

    private static void AssertRealEremexDataGrid(Control preview, string path)
    {
        if (!string.Equals(preview.GetType().FullName, "Eremex.AvaloniaUI.Controls.DataGrid.DataGridControl", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{path} must render the actual Eremex DataGridControl, got '{preview.GetType().FullName}'.");
        }
    }

    private static int GetItemsSourceCount(Control control)
    {
        var value = control.GetType().GetProperty("ItemsSource", BindingFlags.Instance | BindingFlags.Public)?.GetValue(control);
        return value is IEnumerable enumerable ? enumerable.Cast<object>().Count() : 0;
    }

    private static int GetEremexDataGridColumnCount(Control control)
    {
        var columns = control.GetType().GetProperty("Columns", BindingFlags.Instance | BindingFlags.Public)?.GetValue(control) as IEnumerable;
        return columns?.Cast<object>().Count() ?? 0;
    }

    private static void RequireEremexDataGridColumns(Control control, IReadOnlyList<string> expectedHeaders, string path)
    {
        var columns = control.GetType().GetProperty("Columns", BindingFlags.Instance | BindingFlags.Public)?.GetValue(control) as IEnumerable;
        var headers = columns?
            .Cast<object>()
            .Select(column => Convert.ToString(column.GetType().GetProperty("Header", BindingFlags.Instance | BindingFlags.Public)?.GetValue(column), CultureInfo.InvariantCulture) ?? string.Empty)
            .ToList()
            ?? new List<string>();

        foreach (var expectedHeader in expectedHeaders)
        {
            if (!headers.Contains(expectedHeader, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{path} must expose the schema header '{expectedHeader}', actual headers: {string.Join(", ", headers)}.");
            }
        }
    }

    private static void RequireEremexDataGridVisualParts(Control control, string path)
    {
        var visualTypeNames = control.GetVisualDescendants()
            .Select(visual => visual.GetType().FullName ?? visual.GetType().Name)
            .ToList();
        if (!visualTypeNames.Any(name => name.EndsWith("ColumnHeaderControl", StringComparison.Ordinal))
            || !visualTypeNames.Any(name => name.EndsWith("DataGridRowControl", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"{path} did not create the real Eremex header/row visual tree. Actual visuals: {string.Join(", ", visualTypeNames.Take(20))}.");
        }
    }

    private static void RequireControlCanApplyTemplateAndLayout(Control control, string path)
    {
        var host = control as Window ?? new Window
        {
            Width = Math.Max(320, control.Width is > 0 and < double.PositiveInfinity ? control.Width + 80 : 360),
            Height = Math.Max(160, control.Height is > 0 and < double.PositiveInfinity ? control.Height + 80 : 180)
        };
        if (!ReferenceEquals(host, control))
            host.Content = control;

        try
        {
            host.Show();
            host.Measure(new Size(host.Width, host.Height));
            host.Arrange(new Rect(0, 0, host.Width, host.Height));
            host.ApplyTemplate();
            control.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();

            if (!control.IsAttachedToVisualTree())
                throw new InvalidOperationException($"{path} did not attach to the Avalonia visual tree.");
            if (!control.GetVisualDescendants().Any())
                throw new InvalidOperationException($"{path} applied no visual template.");
        }
        catch (MissingMethodException exception)
        {
            throw new InvalidOperationException($"{path} hit an Avalonia ABI mismatch while applying the Eremex template: {exception}", exception);
        }
        finally
        {
            host.Close();
        }
    }

    private static void RequireSingleCoreAvaloniaAssemblyIdentity()
    {
        var duplicateCoreAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => assembly.GetName().Name is "Avalonia.Base" or "Avalonia.Controls" or "Avalonia.Markup.Xaml")
            .GroupBy(assembly => assembly.GetName().Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Select(assembly => AssemblyLoadContext.GetLoadContext(assembly)).Distinct().Count() > 1);
        if (duplicateCoreAssembly is not null)
        {
            throw new InvalidOperationException(
                $"Eremex plugin loaded duplicate core assembly '{duplicateCoreAssembly.Key}' into more than one AssemblyLoadContext.");
        }
    }

    private static Assembly LoadGeneratedApplicationAssembly(string assemblyPath)
    {
        var loadContext = new GeneratedApplicationLoadContext(assemblyPath);
        return loadContext.LoadFromAssemblyPath(assemblyPath);
    }

    private static void RequireGeneratedEremexApplicationRenders(SmokeContext context, string exportFolder)
    {
        DotnetBuild(exportFolder);

        var projectFile = Directory.GetFiles(exportFolder, "*.csproj").SingleOrDefault()
            ?? throw new InvalidOperationException("Generated Eremex project file was not found.");
        var assemblyName = Path.GetFileNameWithoutExtension(projectFile);
        var assemblyPath = Path.Combine(
            exportFolder,
            "bin",
            "Debug",
            "net6.0",
            $"{assemblyName}.dll");
        if (!File.Exists(assemblyPath))
            throw new InvalidOperationException($"Generated Eremex application assembly was not found: {assemblyPath}");

        // LoadFrom mirrors a normal generated application's dependency resolution. In
        // particular, its compiled AXAML and Eremex dependency graph must share the host's
        // Avalonia assemblies, not a second collectible Avalonia universe.
        var assembly = LoadGeneratedApplicationAssembly(assemblyPath);
        var mainWindowType = assembly.GetType($"{context.ViewModel.ExportProjectNamespace}.MainWindow")
            ?? throw new InvalidOperationException("Generated Eremex MainWindow type was not found.");
        var window = Activator.CreateInstance(mainWindowType) as Window
            ?? throw new InvalidOperationException("Generated Eremex MainWindow could not be created as an Avalonia Window.");
        var editor = window.GetLogicalDescendants()
            .OfType<Control>()
            .FirstOrDefault(control => string.Equals(
                control.GetType().FullName,
                "Eremex.AvaloniaUI.Controls.Editors.TextEditor",
                StringComparison.Ordinal));
        if (editor is null)
            throw new InvalidOperationException("Generated Eremex application MainWindow does not contain the actual TextEditor.");

        AssertRealEremexTextEditor(editor, "Generated Eremex application");
        RequireControlCanApplyTemplateAndLayout(window, "Generated Eremex application");
    }

    private static void RequireGeneratedEremexDataGridApplicationRenders(SmokeContext context, string exportFolder)
    {
        DotnetBuild(exportFolder);

        var projectFile = Directory.GetFiles(exportFolder, "*.csproj").SingleOrDefault()
            ?? throw new InvalidOperationException("Generated Eremex DataGrid project file was not found.");
        var assemblyName = Path.GetFileNameWithoutExtension(projectFile);
        var assemblyPath = Path.Combine(exportFolder, "bin", "Debug", "net6.0", $"{assemblyName}.dll");
        if (!File.Exists(assemblyPath))
            throw new InvalidOperationException($"Generated Eremex DataGrid application assembly was not found: {assemblyPath}");

        var assembly = LoadGeneratedApplicationAssembly(assemblyPath);
        var mainWindowType = assembly.GetType($"{context.ViewModel.ExportProjectNamespace}.MainWindow")
            ?? throw new InvalidOperationException("Generated Eremex DataGrid MainWindow type was not found.");
        var window = Activator.CreateInstance(mainWindowType) as Window
            ?? throw new InvalidOperationException("Generated Eremex DataGrid MainWindow could not be created as an Avalonia Window.");
        var grid = window.GetLogicalDescendants()
            .OfType<Control>()
            .FirstOrDefault(control => string.Equals(
                control.GetType().FullName,
                "Eremex.AvaloniaUI.Controls.DataGrid.DataGridControl",
                StringComparison.Ordinal));
        if (grid is null)
            throw new InvalidOperationException("Generated Eremex application MainWindow does not contain the actual DataGridControl.");

        AssertRealEremexDataGrid(grid, "Generated Eremex DataGrid application");
        if (GetItemsSourceCount(grid) == 0)
            throw new InvalidOperationException("Generated Eremex DataGrid application did not receive explicit demo rows.");
        RequireControlCanApplyTemplateAndLayout(window, "Generated Eremex DataGrid application");
    }

    private static void InvokeAsyncPreviewLoader(object host, string methodName, object argument)
    {
        var method = host.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Async preview loader '{host.GetType().Name}.{methodName}' was not found.");
        try
        {
            if (method.Invoke(host, new[] { argument }) is not Task task)
                throw new InvalidOperationException($"Async preview loader '{host.GetType().Name}.{methodName}' did not return Task.");

            task.GetAwaiter().GetResult();
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw new InvalidOperationException($"Async preview loader '{host.GetType().Name}.{methodName}' failed: {ex.InnerException}", ex.InnerException);
        }
    }

    private static void LoadBuiltEremexPlugin(MainWindowViewModel vm)
    {
        var pluginsRoot = Path.Combine(FindRepositoryRoot(), "bin", "Debug", "net6.0", "Plugins");
        if (!Directory.Exists(Path.Combine(pluginsRoot, "EremexDesignerPlugin")))
            throw new InvalidOperationException($"Built Eremex plugin folder was not found: {pluginsRoot}");

        var registry = vm.Registry as DesignerRegistry
            ?? throw new InvalidOperationException("Eremex smoke requires DesignerRegistry.");
        var loader = new PluginLoader(new TraceDesignerLogger());
        loader.LoadFromFolder(pluginsRoot, registry, replaceDiagnostics: true);
        vm.RefreshRegistryBackedCollections();
    }

    private static void SetCustomProperty(DesignControlModel control, string key, object? value)
    {
        var property = control.CustomProperties.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
        if (property is null)
        {
            control.CustomProperties.Add(new DesignPropertyValueModel { Key = key, ValueJson = JsonSerializer.Serialize(value) });
            return;
        }

        property.ValueJson = JsonSerializer.Serialize(value);
    }

    private static IDesignControlNode CreateSmokeControlNode(DesignControlModel control)
    {
        return new SmokeControlNode(
            control.Id,
            control.Type,
            control.Name,
            control.ParentId,
            control.DescriptorId,
            control.PluginId,
            control.PluginVersion,
            new Dictionary<string, object?>
            {
                ["Width"] = control.Width,
                ["Height"] = control.Height,
                ["X"] = control.X,
                ["Y"] = control.Y,
                ["Margin"] = control.Margin,
                ["Opacity"] = control.Opacity,
                ["IsVisible"] = control.IsVisible
            },
            control.CustomProperties.ToDictionary(property => property.Key, property => property.ValueJson, StringComparer.OrdinalIgnoreCase));
    }

    private static void ConfigureGridLayoutExport(MainWindowViewModel vm)
    {
        vm.SurfaceLayoutMode = DesignerLayoutModes.Grid;
        vm.SurfaceLayoutColumns = 2;
        vm.SurfaceLayoutRows = 2;
        vm.SurfaceGridColumnDefinitions = "Auto,*";
        vm.SurfaceGridRowDefinitions = "Auto,*";
        var title = Control(DesignerControlTypes.TextBlock, "GridTitle", 0, 0, 220, 30, text: "Grid layout");
        title.GridRow = 0;
        title.GridColumn = 0;
        title.GridColumnSpan = 2;
        title.HorizontalAlignment = DesignerLayoutModes.AlignStretch;
        vm.Controls.Add(title);

        var input = Control(DesignerControlTypes.TextBox, "GridInput", 0, 0, 240, 38, placeholder: "Input");
        input.GridRow = 1;
        input.GridColumn = 0;
        input.Margin = "0,12,12,0";
        vm.Controls.Add(input);

        var submit = Control(DesignerControlTypes.Button, "GridSubmit", 0, 0, 140, 40, text: "Submit", background: "#2563EB", foreground: "#FFFFFF", border: "#1D4ED8", radius: 10);
        submit.GridRow = 1;
        submit.GridColumn = 1;
        submit.HorizontalAlignment = DesignerLayoutModes.AlignRight;
        submit.Margin = "0,12,0,0";
        vm.Controls.Add(submit);
    }

    private static void AssertGridLayoutExport(SmokeContext context)
    {
        RequireContains(context.Xaml, "<Grid x:Name=\"RootLayout\"", "Grid layout export should use Avalonia Grid as the root.");
        RequireContains(context.Xaml, "ColumnDefinitions=\"Auto,*\"", "Grid column definitions should be exported.");
        RequireContains(context.Xaml, "RowDefinitions=\"Auto,*\"", "Grid row definitions should be exported.");
        RequireContains(context.Xaml, "Grid.Row=\"1\" Grid.Column=\"1\"", "Child Grid.Row/Grid.Column placement should be exported.");
        RequireNotContains(context.Xaml, "Canvas.Left", "Grid layout children should not export Canvas.Left.");
        RequireNotContains(context.Xaml, "primitives:UniformGrid", "Grid layout export should not use UniformGrid.");
    }

    private static void ConfigureStackPanelLayoutExport(MainWindowViewModel vm)
    {
        vm.SurfaceLayoutMode = DesignerLayoutModes.Stack;
        vm.SurfaceLayoutOrientation = DesignerLayoutModes.Vertical;
        vm.SurfaceLayoutSpacing = 8;

        var input = Control(DesignerControlTypes.TextBox, "StackInput", 0, 0, 260, 38, placeholder: "Email");
        input.StackOrder = 0;
        vm.Controls.Add(input);

        var submit = Control(DesignerControlTypes.Button, "StackSubmit", 0, 0, 140, 40, text: "Submit", background: "#2563EB", foreground: "#FFFFFF", border: "#1D4ED8", radius: 10);
        submit.StackOrder = 1;
        submit.Margin = "0,4,0,0";
        vm.Controls.Add(submit);
    }

    private static void AssertStackPanelLayoutExport(SmokeContext context)
    {
        RequireContains(context.Xaml, "<StackPanel x:Name=\"RootLayout\" Orientation=\"Vertical\" Spacing=\"8\"", "StackPanel layout should export root StackPanel with orientation and spacing.");
        RequireContains(context.Xaml, "StackInput", "First StackPanel child should be exported.");
        RequireContains(context.Xaml, "StackSubmit", "Second StackPanel child should be exported.");
        RequireNotContains(context.Xaml, "Canvas.Left", "StackPanel layout children should not export Canvas.Left.");
        RequireNotContains(context.Xaml, "Canvas.Top", "StackPanel layout children should not export Canvas.Top.");
    }

    private static void ConfigureLayoutContainerExport(MainWindowViewModel vm)
    {
        var border = Control(DesignerControlTypes.Border, "DetailsPanel", 40, 40, 360, 160, background: "Transparent", border: "#CBD5E1", radius: 8);
        border.ChildLayoutMode = DesignerLayoutModes.Stack;
        border.LayoutOrientation = DesignerLayoutModes.Vertical;
        border.LayoutSpacing = 6;
        border.Padding = 12;
        vm.Controls.Add(border);

        var title = Control(DesignerControlTypes.TextBlock, "DetailsTitle", 0, 0, 240, 28, text: "Details");
        title.ParentId = border.Id;
        title.StackOrder = 0;
        vm.Controls.Add(title);

        var input = Control(DesignerControlTypes.TextBox, "DetailsInput", 0, 0, 260, 38, placeholder: "Name");
        input.ParentId = border.Id;
        input.StackOrder = 1;
        vm.Controls.Add(input);
    }

    private static void AssertLayoutContainerExport(SmokeContext context)
    {
        RequireContains(context.Xaml, "DetailsPanel", "Layout container should be exported.");
        RequireContains(context.Xaml, "<StackPanel Orientation=\"Vertical\" Spacing=\"6\"", "Border layout container should export inner StackPanel.");
        RequireContains(context.Xaml, "DetailsTitle", "Container child TextBlock should be exported.");
        RequireContains(context.Xaml, "DetailsInput", "Container child TextBox should be exported.");
    }

    private static void ConfigureLayoutConversionExport(MainWindowViewModel vm)
    {
        var first = Control(DesignerControlTypes.TextBox, "ConvertedInput", 40, 40, 260, 38, placeholder: "Converted");
        var second = Control(DesignerControlTypes.Button, "ConvertedButton", 40, 96, 160, 40, text: "Save", background: "#2563EB", foreground: "#FFFFFF", border: "#1D4ED8", radius: 10);
        vm.Controls.Add(first);
        vm.Controls.Add(second);
        vm.SelectControls(new[] { first, second }, second);
        vm.ConvertSelectionToStackPanelEditorCommand?.Execute(null);
    }

    private static void AssertLayoutConversionExport(SmokeContext context)
    {
        RequireContains(context.Xaml, "StackPanel", "Converted selection should export as a StackPanel container.");
        RequireContains(context.Xaml, "ConvertedInput", "Converted TextBox should be preserved.");
        RequireContains(context.Xaml, "ConvertedButton", "Converted Button should be preserved.");
        RequireNotContains(context.DiagnosticsText, "outside the parent grid", "Conversion should not create invalid Grid placement.");
    }

    private static void ConfigureResponsiveStackPanelExport(MainWindowViewModel vm)
    {
        vm.LayoutExportMode = MainWindowViewModel.LayoutExportModeResponsive;
        vm.SurfaceLayoutSpacing = 14;
        vm.Controls.Add(Control(DesignerControlTypes.TextBlock, "HeaderText", 32, 32, 360, 30, text: "Responsive form"));
        vm.Controls.Add(Control(DesignerControlTypes.TextBox, "EmailTextBox", 32, 84, 320, 38, placeholder: "Email"));
        vm.Controls.Add(Control(DesignerControlTypes.Button, "SubmitButton", 32, 142, 160, 42, text: "Submit", background: "#2563EB", foreground: "#FFFFFF", border: "#1D4ED8", radius: 10));
    }

    private static void AssertResponsiveStackPanelExport(SmokeContext context)
    {
        RequireContains(context.Xaml, "<StackPanel", "Responsive simple form should export a StackPanel.");
        RequireContains(context.ChecklistText, "Layout export mode: Responsive StackPanel", "Checklist should report Responsive StackPanel.");
    }

    private static void ConfigureResponsiveCanvasFallbackExport(MainWindowViewModel vm)
    {
        vm.LayoutExportMode = MainWindowViewModel.LayoutExportModeResponsive;
        vm.Controls.Add(Control(DesignerControlTypes.TextBlock, "FirstText", 32, 32, 220, 80, text: "First"));
        vm.Controls.Add(Control(DesignerControlTypes.TextBox, "OverlappingTextBox", 42, 70, 260, 38, placeholder: "Overlap"));
    }

    private static void AssertResponsiveCanvasFallbackExport(SmokeContext context)
    {
        RequireContains(context.Xaml, "<Canvas", "Overlapping responsive form should fallback to Canvas.");
        RequireContains(context.ChecklistText, "Layout export mode: Canvas fallback", "Checklist should report Canvas fallback.");
        RequireContains(context.ChecklistText, "пересекаются", "Checklist should explain overlap fallback.");
    }

    private static BindingSourceModel ProductsSource()
    {
        var source = new BindingSourceModel
        {
            Id = "products-source",
            Name = "ProductsSource",
            Path = "Products",
            ItemTypeName = "ProductRow",
            Description = "Smoke test products source."
        };
        source.Fields.Add(Field("Title", "Title", "Keyboard", "string", "2*"));
        source.Fields.Add(Field("Price", "Price", "149.90", "decimal", "*"));
        source.Fields.Add(Field("Count", "Count", "3", "int", "*"));
        return source;
    }

    private static BindingFieldModel Field(string header, string path, string sample, string typeName, string width)
    {
        return new BindingFieldModel
        {
            Header = header,
            Path = path,
            SampleValue = sample,
            TypeName = typeName,
            Width = width,
            IsVisible = true,
            AllowSort = true,
            AllowFilter = true
        };
    }

    private static DesignControlModel DataGrid(string name, string bindingSourceId, double x, double y, double width, double height)
    {
        return new DesignControlModel
        {
            Type = DesignerControlTypes.DataGrid,
            DescriptorId = DesignerControlTypes.DataGrid,
            Name = name,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            BindingSourceId = bindingSourceId,
            Text = "DataGrid",
            Background = "#FFFFFF",
            BorderBrush = "#CBD5E1",
            BorderThickness = 1,
            AutoGenerateColumns = false,
            DataGridShowHeader = true,
            DataGridShowRowLines = true,
            DataGridShowColumnLines = true,
            DataGridShowAlternatingRows = true,
            ShowFilterRow = false,
            ShowGroupPanel = false,
            ShowFooter = false
        };
    }

    private static DesignControlModel Control(
        string type,
        string name,
        double x,
        double y,
        double width,
        double height,
        string text = "",
        string placeholder = "",
        string background = "#FFFFFF",
        string foreground = "#0F172A",
        string border = "#94A3B8",
        double radius = 6,
        string pluginId = "",
        string pluginVersion = "")
    {
        return new DesignControlModel
        {
            Type = type,
            DescriptorId = type,
            Name = name,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            Text = text,
            PlaceholderText = placeholder,
            Background = background,
            Foreground = foreground,
            BorderBrush = border,
            CornerRadius = radius,
            PluginId = pluginId,
            PluginVersion = pluginVersion
        };
    }

    private static IEnumerable<ProjectExplorerItemModel> FlattenExplorerItems(IEnumerable<ProjectExplorerItemModel> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in FlattenExplorerItems(item.Children))
                yield return child;
        }
    }

    private static IEnumerable<string> GetProjectExplorerFormIds(MainWindowViewModel vm)
    {
        return FlattenExplorerItems(vm.ProjectExplorerItems)
            .Where(item => string.Equals(item.ItemType, "Form", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.TargetId)
            .Where(id => !string.IsNullOrWhiteSpace(id));
    }

    private static void ForceFullGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Views"))
                && Directory.Exists(Path.Combine(directory.FullName, "ViewModels")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static void WriteAvaloniaProject(SmokeContext context)
    {
        Directory.CreateDirectory(context.ProjectPath);
        File.WriteAllText(Path.Combine(context.ProjectPath, $"{context.Scenario.Name}.xaml.txt"), context.Xaml, Encoding.UTF8);
        File.WriteAllText(Path.Combine(context.ProjectPath, $"{context.Scenario.Name}.cs.txt"), context.CSharp, Encoding.UTF8);
        foreach (var generatedFile in context.GeneratedFiles.Where(file =>
                     file.Path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase)
                     || file.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            var targetPath = Path.Combine(context.ProjectPath, generatedFile.Path.Replace('/', Path.DirectorySeparatorChar));
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(targetPath, generatedFile.Content, Encoding.UTF8);
        }

        if (!File.Exists(Path.Combine(context.ProjectPath, "MainWindow.axaml")))
            File.WriteAllText(Path.Combine(context.ProjectPath, "MainWindow.axaml"), context.Xaml, Encoding.UTF8);
        if (!File.Exists(Path.Combine(context.ProjectPath, "MainWindow.axaml.cs")))
            File.WriteAllText(Path.Combine(context.ProjectPath, "MainWindow.axaml.cs"), context.CSharp, Encoding.UTF8);
        File.WriteAllText(Path.Combine(context.ProjectPath, "App.axaml"), BuildAppXaml(context.ViewModel.ExportProjectNamespace, context.Scenario.RequiresRealDataGrid), Encoding.UTF8);
        File.WriteAllText(Path.Combine(context.ProjectPath, "App.axaml.cs"), BuildAppCode(context.ViewModel.ExportProjectNamespace), Encoding.UTF8);
        File.WriteAllText(Path.Combine(context.ProjectPath, "Program.cs"), BuildProgramCode(context.ViewModel.ExportProjectNamespace), Encoding.UTF8);
        File.WriteAllText(Path.Combine(context.ProjectPath, $"{context.Scenario.Name}.csproj"), BuildProjectFile(context), Encoding.UTF8);
        File.WriteAllText(Path.Combine(context.ProjectPath, "NuGet.config"), BuildNuGetConfig(), Encoding.UTF8);
        File.WriteAllText(Path.Combine(context.ProjectPath, "smoke-summary.txt"), BuildSummary(context), Encoding.UTF8);
    }

    private static string BuildNuGetConfig()
    {
        return @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <packageSources>
    <clear />
    <add key=""nuget.org"" value=""https://api.nuget.org/v3/index.json"" protocolVersion=""3"" />
  </packageSources>
  <packageSourceMapping>
    <clear />
  </packageSourceMapping>
</configuration>
";
    }

    private static string BuildProjectFile(SmokeContext context)
    {
        var requiredPackages = context.ViewModel.RequiredPackages
            .GroupBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (context.Scenario.RequiresRealDataGrid
            && !requiredPackages.Any(package => string.Equals(package.Id, "Avalonia.Controls.DataGrid", StringComparison.OrdinalIgnoreCase)))
        {
            requiredPackages.Add(new RequiredPackageModel
            {
                Id = "Avalonia.Controls.DataGrid",
                Version = AvaloniaVersion
            });
        }

        var packageLines = new StringBuilder();
        foreach (var package in requiredPackages)
        {
            var version = string.IsNullOrWhiteSpace(package.Version) ? AvaloniaVersion : package.Version;
            packageLines.AppendLine($@"    <PackageReference Include=""{package.Id}"" Version=""{version}"" />");
        }

        return $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net6.0</TargetFramework>
    <Nullable>enable</Nullable>
    <AvaloniaUseCompiledBindingsByDefault>false</AvaloniaUseCompiledBindingsByDefault>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include=""Avalonia"" Version=""{AvaloniaVersion}"" />
    <PackageReference Include=""Avalonia.Desktop"" Version=""{AvaloniaDesktopVersion}"" />
    <PackageReference Include=""Avalonia.Themes.Fluent"" Version=""{AvaloniaVersion}"" />
    <PackageReference Include=""Avalonia.Fonts.Inter"" Version=""{AvaloniaDesktopVersion}"" />
{packageLines.ToString().TrimEnd()}
  </ItemGroup>
</Project>
";
    }

    private static string BuildAppXaml(string ns, bool includeDataGridTheme)
    {
        var dataGridStyleInclude = includeDataGridTheme
            ? "    <StyleInclude Source=\"avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml\" />\n"
            : "";
        return $@"<Application xmlns=""https://github.com/avaloniaui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
             x:Class=""{ns}.App""
             RequestedThemeVariant=""Default"">
  <Application.Styles>
    <FluentTheme />
{dataGridStyleInclude.TrimEnd()}
  </Application.Styles>
</Application>
";
    }

    private static string BuildAppCode(string ns)
    {
        return @"using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace {Namespace};

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }
}
".Replace("{Namespace}", ns);
    }

    private static string BuildProgramCode(string ns)
    {
        return @"using Avalonia;
using System;

namespace {Namespace};

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
".Replace("{Namespace}", ns);
    }

    private static string BuildSummary(SmokeContext context)
    {
        return $@"Scenario: {context.Scenario.Name}
Project: {context.ProjectPath}

Checklist:
{context.ChecklistText}

Diagnostics:
{context.DiagnosticsText}
";
    }

    private static void DotnetBuild(string projectPath)
    {
        var projectFile = Directory.GetFiles(projectPath, "*.csproj").Single();
        var result = RunProcess("dotnet", $"build \"{projectFile}\"", projectPath);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"dotnet build failed.{Environment.NewLine}{result.Output}");
    }

    private static TemporaryExportProject ExportToTemporaryProject(SmokeContext context)
    {
        var exportFolder = Path.Combine(Path.GetTempPath(), "FormDesignerSmokeExports", context.Scenario.Name, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(exportFolder);
        context.ViewModel.ExportCurrentResultToProjectAsync(exportFolder).GetAwaiter().GetResult();
        return new TemporaryExportProject(exportFolder);
    }

    private static string NormalizeNugetConfigForCompare(string value)
    {
        return value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim();
    }

    private static void RequireFileExists(string path, string message)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"{message} Missing: {path}");
    }

    private static void RequireGeneratedViewModelCollectionHasRows(SmokeContext context, string propertyName, int minimumRows)
    {
        var rowCount = GetGeneratedViewModelCollectionRowCount(context, propertyName);
        if (rowCount < minimumRows)
            throw new InvalidOperationException($"Generated runtime DataGrid collection is empty: {propertyName} rows={rowCount}, expected at least {minimumRows}.");
    }

    private static void RequireGeneratedViewModelCollectionRowCount(SmokeContext context, string propertyName, int expectedRows)
    {
        var rowCount = GetGeneratedViewModelCollectionRowCount(context, propertyName);
        if (rowCount != expectedRows)
            throw new InvalidOperationException($"Generated runtime DataGrid collection row count mismatch: {propertyName} rows={rowCount}, expected {expectedRows}.");
    }

    private static int GetGeneratedViewModelCollectionRowCount(SmokeContext context, string propertyName)
    {
        DotnetBuild(context.ProjectPath);

        var assemblyPath = Path.Combine(
            context.ProjectPath,
            "bin",
            "Debug",
            "net6.0",
            $"{context.Scenario.Name}.dll");
        if (!File.Exists(assemblyPath))
            throw new InvalidOperationException($"Generated assembly was not found: {assemblyPath}");

        var loadContext = new AssemblyLoadContext($"smoke-runtime-{context.Scenario.Name}-{Guid.NewGuid():N}", isCollectible: true);
        var resolver = new AssemblyDependencyResolver(assemblyPath);
        loadContext.Resolving += (_, assemblyName) =>
        {
            var resolvedPath = resolver.ResolveAssemblyToPath(assemblyName);
            return resolvedPath is null ? null : loadContext.LoadFromAssemblyPath(resolvedPath);
        };

        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            var viewModelTypeName = $"{context.ViewModel.ExportProjectNamespace}.MainWindowViewModel";
            var viewModelType = assembly.GetType(viewModelTypeName)
                ?? throw new InvalidOperationException($"Generated ViewModel type was not found: {viewModelTypeName}");
            var instance = Activator.CreateInstance(viewModelType)
                ?? throw new InvalidOperationException($"Generated ViewModel could not be created: {viewModelTypeName}");
            var property = viewModelType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException($"Generated ViewModel property was not found: {viewModelTypeName}.{propertyName}");
            var value = property.GetValue(instance);
            if (value is not IEnumerable enumerable)
                throw new InvalidOperationException($"Generated ViewModel property is not enumerable: {viewModelTypeName}.{propertyName}");

            return enumerable.Cast<object?>().Count();
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static string CreateLinqToSqlMetadataAssembly(string assemblyName, string namespaceName, string typeName, string tableName)
    {
        var cacheKey = $"{assemblyName}|{namespaceName}|{typeName}|{tableName}";
        if (LinqToSqlSmokeDllCache.TryGetValue(cacheKey, out var cachedPath) && File.Exists(cachedPath))
            return cachedPath;

        var root = Path.Combine(Path.GetTempPath(), "FormDesignerSmokeDlls", assemblyName);
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);

        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, $"{assemblyName}.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">" + Environment.NewLine +
            "  <PropertyGroup>" + Environment.NewLine +
            "    <TargetFramework>net6.0</TargetFramework>" + Environment.NewLine +
            "    <Nullable>enable</Nullable>" + Environment.NewLine +
            "  </PropertyGroup>" + Environment.NewLine +
            "</Project>" + Environment.NewLine,
            Encoding.UTF8);

        File.WriteAllText(
            Path.Combine(root, "Models.cs"),
            "using System;" + Environment.NewLine +
            Environment.NewLine +
            "namespace System.Data.Linq.Mapping" + Environment.NewLine +
            "{" + Environment.NewLine +
            "    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]" + Environment.NewLine +
            "    public sealed class TableAttribute : Attribute" + Environment.NewLine +
            "    {" + Environment.NewLine +
            "        public string Name { get; set; } = \"\";" + Environment.NewLine +
            "    }" + Environment.NewLine +
            Environment.NewLine +
            "    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]" + Environment.NewLine +
            "    public sealed class ColumnAttribute : Attribute" + Environment.NewLine +
            "    {" + Environment.NewLine +
            "        public string Name { get; set; } = \"\";" + Environment.NewLine +
            "        public string DbType { get; set; } = \"\";" + Environment.NewLine +
            "        public bool IsPrimaryKey { get; set; }" + Environment.NewLine +
            "        public bool CanBeNull { get; set; } = true;" + Environment.NewLine +
            "    }" + Environment.NewLine +
            Environment.NewLine +
            "    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]" + Environment.NewLine +
            "    public sealed class AssociationAttribute : Attribute" + Environment.NewLine +
            "    {" + Environment.NewLine +
            "        public string Name { get; set; } = \"\";" + Environment.NewLine +
            "    }" + Environment.NewLine +
            "}" + Environment.NewLine +
            Environment.NewLine +
            $"namespace {namespaceName}" + Environment.NewLine +
            "{" + Environment.NewLine +
            "    using System.Data.Linq.Mapping;" + Environment.NewLine +
            Environment.NewLine +
            $"    [Table(Name = \"{tableName}\")]" + Environment.NewLine +
            $"    public sealed class {typeName}" + Environment.NewLine +
            "    {" + Environment.NewLine +
            "        [Column(Name = \"CustomerId\", DbType = \"Int NOT NULL\", IsPrimaryKey = true, CanBeNull = false)]" + Environment.NewLine +
            "        public int Id { get; set; }" + Environment.NewLine +
            Environment.NewLine +
            "        [Column(Name = \"CustomerName\", DbType = \"NVarChar(100)\", CanBeNull = false)]" + Environment.NewLine +
            "        public string Name { get; set; } = \"\";" + Environment.NewLine +
            Environment.NewLine +
            "        [Column(Name = \"EmailAddress\", DbType = \"NVarChar(256)\", CanBeNull = true)]" + Environment.NewLine +
            "        public string? Email { get; set; }" + Environment.NewLine +
            Environment.NewLine +
            "        [Association(Name = \"FK_Customers_Orders\")]" + Environment.NewLine +
            "        public object? Orders { get; set; }" + Environment.NewLine +
            "    }" + Environment.NewLine +
            "}" + Environment.NewLine,
            Encoding.UTF8);

        DotnetBuild(root);
        var assemblyPath = Path.Combine(root, "bin", "Debug", "net6.0", $"{assemblyName}.dll");
        if (!File.Exists(assemblyPath))
            throw new InvalidOperationException($"Smoke LINQ to SQL DLL was not built: {assemblyPath}");

        LinqToSqlSmokeDllCache[cacheKey] = assemblyPath;
        return assemblyPath;
    }

    private static string CreateLinqToSqlPreviewRowsAssembly(string assemblyName, string namespaceName, string typeName, string tableName, int rowCount)
    {
        var cacheKey = $"{assemblyName}|{namespaceName}|{typeName}|{tableName}|preview|{rowCount}";
        if (LinqToSqlSmokeDllCache.TryGetValue(cacheKey, out var cachedPath) && File.Exists(cachedPath))
            return cachedPath;

        var root = Path.Combine(Path.GetTempPath(), "FormDesignerSmokeDlls", assemblyName);
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);

        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, $"{assemblyName}.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">" + Environment.NewLine +
            "  <PropertyGroup>" + Environment.NewLine +
            "    <TargetFramework>net6.0</TargetFramework>" + Environment.NewLine +
            "    <Nullable>enable</Nullable>" + Environment.NewLine +
            "  </PropertyGroup>" + Environment.NewLine +
            "</Project>" + Environment.NewLine,
            Encoding.UTF8);

        var code = new StringBuilder();
        code.AppendLine("using System;");
        code.AppendLine("using System.Collections.Generic;");
        code.AppendLine();
        code.AppendLine("namespace System.Data.Linq.Mapping");
        code.AppendLine("{");
        code.AppendLine("    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]");
        code.AppendLine("    public sealed class TableAttribute : Attribute");
        code.AppendLine("    {");
        code.AppendLine("        public string Name { get; set; } = \"\";");
        code.AppendLine("    }");
        code.AppendLine();
        code.AppendLine("    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]");
        code.AppendLine("    public sealed class ColumnAttribute : Attribute");
        code.AppendLine("    {");
        code.AppendLine("        public string Name { get; set; } = \"\";");
        code.AppendLine("        public string DbType { get; set; } = \"\";");
        code.AppendLine("        public bool IsPrimaryKey { get; set; }");
        code.AppendLine("        public bool CanBeNull { get; set; } = true;");
        code.AppendLine("    }");
        code.AppendLine("}");
        code.AppendLine();
        code.AppendLine($"namespace {namespaceName}");
        code.AppendLine("{");
        code.AppendLine("    using System.Data.Linq.Mapping;");
        code.AppendLine();
        code.AppendLine($"    [Table(Name = \"{tableName}\")]");
        code.AppendLine($"    public sealed class {typeName}");
        code.AppendLine("    {");
        code.AppendLine("        [Column(Name = \"OrderId\", DbType = \"Int NOT NULL\", IsPrimaryKey = true, CanBeNull = false)]");
        code.AppendLine("        public int Id { get; set; }");
        code.AppendLine();
        code.AppendLine("        [Column(Name = \"CustomerName\", DbType = \"NVarChar(120)\", CanBeNull = false)]");
        code.AppendLine("        public string Name { get; set; } = \"\";");
        code.AppendLine();
        code.AppendLine("        [Column(Name = \"CreatedAt\", DbType = \"DateTime NOT NULL\", CanBeNull = false)]");
        code.AppendLine("        public DateTime CreatedAt { get; set; }");
        code.AppendLine();
        code.AppendLine($"        public static IEnumerable<{typeName}> GetPreviewRows()");
        code.AppendLine("        {");
        code.AppendLine($"            for (var index = 1; index <= {rowCount}; index++)");
        code.AppendLine("            {");
        code.AppendLine($"                yield return new {typeName}");
        code.AppendLine("                {");
        code.AppendLine("                    Id = index,");
        code.AppendLine("                    Name = $\"Real order {index:000}\",");
        code.AppendLine("                    CreatedAt = new DateTime(2024, 1, 1).AddDays(index)");
        code.AppendLine("                };");
        code.AppendLine("            }");
        code.AppendLine("        }");
        code.AppendLine("    }");
        code.AppendLine("}");

        File.WriteAllText(Path.Combine(root, "Models.cs"), code.ToString(), Encoding.UTF8);
        DotnetBuild(root);
        var assemblyPath = Path.Combine(root, "bin", "Debug", "net6.0", $"{assemblyName}.dll");
        if (!File.Exists(assemblyPath))
            throw new InvalidOperationException($"Smoke preview rows DLL was not built: {assemblyPath}");

        LinqToSqlSmokeDllCache[cacheKey] = assemblyPath;
        return assemblyPath;
    }

    private static string CreateManyTableMetadataAssembly(string assemblyName, string namespaceName, int tableCount)
    {
        var cacheKey = $"{assemblyName}|{namespaceName}|many|{tableCount}";
        if (LinqToSqlSmokeDllCache.TryGetValue(cacheKey, out var cachedPath) && File.Exists(cachedPath))
            return cachedPath;

        var root = Path.Combine(Path.GetTempPath(), "FormDesignerSmokeDlls", assemblyName);
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);

        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, $"{assemblyName}.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">" + Environment.NewLine +
            "  <PropertyGroup>" + Environment.NewLine +
            "    <TargetFramework>net6.0</TargetFramework>" + Environment.NewLine +
            "    <Nullable>enable</Nullable>" + Environment.NewLine +
            "  </PropertyGroup>" + Environment.NewLine +
            "</Project>" + Environment.NewLine,
            Encoding.UTF8);

        var code = new StringBuilder();
        code.AppendLine("using System;");
        code.AppendLine();
        code.AppendLine("namespace System.Data.Linq.Mapping");
        code.AppendLine("{");
        code.AppendLine("    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]");
        code.AppendLine("    public sealed class TableAttribute : Attribute");
        code.AppendLine("    {");
        code.AppendLine("        public string Name { get; set; } = \"\";");
        code.AppendLine("    }");
        code.AppendLine();
        code.AppendLine("    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]");
        code.AppendLine("    public sealed class ColumnAttribute : Attribute");
        code.AppendLine("    {");
        code.AppendLine("        public string Name { get; set; } = \"\";");
        code.AppendLine("        public string DbType { get; set; } = \"\";");
        code.AppendLine("        public bool IsPrimaryKey { get; set; }");
        code.AppendLine("        public bool CanBeNull { get; set; } = true;");
        code.AppendLine("    }");
        code.AppendLine("}");
        code.AppendLine();
        code.AppendLine($"namespace {namespaceName}");
        code.AppendLine("{");
        code.AppendLine("    using System.Data.Linq.Mapping;");
        for (var index = 0; index < tableCount; index++)
        {
            code.AppendLine();
            code.AppendLine($"    [Table(Name = \"dbo.Table{index:D2}\")]");
            code.AppendLine($"    public sealed class Table{index:D2}Row");
            code.AppendLine("    {");
            code.AppendLine("        [Column(Name = \"Id\", DbType = \"Int NOT NULL\", IsPrimaryKey = true, CanBeNull = false)]");
            code.AppendLine("        public int Id { get; set; }");
            code.AppendLine();
            code.AppendLine("        [Column(Name = \"Name\", DbType = \"NVarChar(100)\", CanBeNull = false)]");
            code.AppendLine("        public string Name { get; set; } = \"\";");
            code.AppendLine();
            code.AppendLine("        [Column(Name = \"Amount\", DbType = \"Decimal(18,2)\", CanBeNull = true)]");
            code.AppendLine("        public decimal? Amount { get; set; }");
            code.AppendLine("    }");
        }
        code.AppendLine("}");

        File.WriteAllText(Path.Combine(root, "Models.cs"), code.ToString(), Encoding.UTF8);
        DotnetBuild(root);
        var assemblyPath = Path.Combine(root, "bin", "Debug", "net6.0", $"{assemblyName}.dll");
        if (!File.Exists(assemblyPath))
            throw new InvalidOperationException($"Smoke many-table DLL was not built: {assemblyPath}");

        LinqToSqlSmokeDllCache[cacheKey] = assemblyPath;
        return assemblyPath;
    }

    private static ProcessResult RunProcess(string fileName, string arguments, string workingDirectory)
    {
        var output = new StringBuilder();
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
                output.AppendLine(args.Data);
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
                output.AppendLine(args.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, output.ToString());
    }

    private static void RequireContains(string text, string expected, string message)
    {
        if (!text.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException(message);
    }

    private static void RequireNotContains(string text, string unexpected, string message)
    {
        if (text.Contains(unexpected, StringComparison.Ordinal))
            throw new InvalidOperationException(message);
    }

    private static void RequireEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} Expected: {expected}; actual: {actual}");
    }

    private static void RequireNotEqual<T>(T unexpected, T actual, string message)
    {
        if (EqualityComparer<T>.Default.Equals(unexpected, actual))
            throw new InvalidOperationException($"{message} Unexpected value: {unexpected}");
    }

    private static void RequireGeneratedMainWindowViewModelAssignment(SmokeContext context, string message)
    {
        if (context.CSharp.Contains("DataContext = new MainWindowViewModel();", StringComparison.Ordinal)
            || (context.CSharp.Contains("var viewModel = new MainWindowViewModel();", StringComparison.Ordinal)
                && context.CSharp.Contains("DataContext = viewModel;", StringComparison.Ordinal)))
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static void RequireTraceEvent(SmokeContext context, string eventName)
    {
        if (context.ViewModel.InteractionTraceEntries.Any(entry => string.Equals(entry.EventName, eventName, StringComparison.OrdinalIgnoreCase)))
            return;

        var trace = string.Join(" | ", context.ViewModel.InteractionTraceEntries.Take(12).Select(entry => entry.Summary));
        throw new InvalidOperationException($"Expected trace event '{eventName}' was not emitted. Recent trace: {trace}");
    }

    private static void RequireNoTraceEvent(SmokeContext context, string eventName)
    {
        if (!context.ViewModel.InteractionTraceEntries.Any(entry => string.Equals(entry.EventName, eventName, StringComparison.OrdinalIgnoreCase)))
            return;

        throw new InvalidOperationException($"Trace event '{eventName}' should not be emitted.");
    }

    private static string GetGeneratedFilesText(SmokeContext context)
    {
        return string.Join(Environment.NewLine, context.GeneratedFiles.Select(file => file.Content));
    }

    private static string GetRequiredPackagesText(SmokeContext context)
    {
        return string.Join(Environment.NewLine, context.ViewModel.RequiredPackages.Select(package => $"{package.Id} {package.Version}"));
    }

    private static GeneratedFileModel RequireGeneratedFile(SmokeContext context, string path)
    {
        return context.GeneratedFiles.FirstOrDefault(file => string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Generated file missing: {path}");
    }

    private static void RequireSecondaryGeneratedFiles(SmokeContext context)
    {
        var secondaryXaml = context.GeneratedFiles
            .Where(file => file.Path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(file.Path, "MainWindow.axaml", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var secondaryCode = context.GeneratedFiles
            .Where(file => file.Path.EndsWith(".axaml.cs", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(file.Path, "MainWindow.axaml.cs", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (secondaryXaml.Count == 0 || secondaryCode.Count == 0)
        {
            var files = string.Join(", ", context.GeneratedFiles.Select(file => file.Path));
            throw new InvalidOperationException($"Expected secondary generated form files. Files: {files}");
        }
    }

    private static void SafeCleanDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = new DirectoryInfo(fullPath);
        if (!directory.Exists)
            return;

        if (!fullPath.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}smoke-tests", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to clean unexpected directory: {fullPath}");

        foreach (var childDirectory in directory.GetDirectories())
            DeleteWithRetry(() => childDirectory.Delete(recursive: true), childDirectory.FullName);

        foreach (var file in directory.GetFiles())
            DeleteWithRetry(file.Delete, file.FullName);
    }

    private static void DeleteWithRetry(Action delete, string path)
    {
        const int attempts = 5;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                delete();
                return;
            }
            catch (IOException) when (attempt < attempts)
            {
                Thread.Sleep(250 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < attempts)
            {
                Thread.Sleep(250 * attempt);
            }
        }

        throw new IOException($"Could not clean smoke-test artifact path: {path}");
    }

    private static void PruneSmokeRuns(string artifactsRoot, string currentRunRoot)
    {
        var root = new DirectoryInfo(Path.GetFullPath(artifactsRoot));
        if (!root.Exists)
            return;

        var current = Path.GetFullPath(currentRunRoot);
        foreach (var staleRun in root.GetDirectories()
                     .OrderByDescending(directory => directory.LastWriteTimeUtc)
                     .ThenByDescending(directory => directory.Name, StringComparer.OrdinalIgnoreCase)
                     .Skip(SmokeRunsToKeep))
        {
            var fullPath = Path.GetFullPath(staleRun.FullName);
            if (string.Equals(fullPath, current, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!fullPath.StartsWith(root.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                continue;

            DeleteWithRetry(() => staleRun.Delete(recursive: true), staleRun.FullName);
        }
    }

    private sealed record NewProjectOldState(
        IReadOnlyList<string> OldFormIds,
        IReadOnlyList<string> OldControlIds);

    private sealed record NamedWeakReference(
        string Kind,
        string Id,
        WeakReference Reference);

    private sealed record NewProjectWeakState(
        IReadOnlyList<string> OldFormIds,
        IReadOnlyList<string> OldControlIds,
        IReadOnlyList<NamedWeakReference> WeakReferences);

    private sealed record SmokeScenario(
        string Name,
        Action<MainWindowViewModel> Configure,
        Action<SmokeContext> Assert,
        bool RequiresRealDataGrid = false);

    private sealed record SmokeContext(
        SmokeScenario Scenario,
        MainWindowViewModel ViewModel,
        string ProjectPath,
        string Xaml,
        string CSharp,
        IReadOnlyList<GeneratedFileModel> GeneratedFiles,
        string ChecklistText,
        string DiagnosticsText);

    private sealed record ProcessResult(int ExitCode, string Output);

    private static T InvokePrivateTask<T>(object target, string methodName, params object?[] arguments)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Private task method '{methodName}' was not found.");
        var task = method.Invoke(target, arguments) as Task<T>
            ?? throw new InvalidOperationException($"Private method '{methodName}' did not return Task<{typeof(T).Name}>.");
        return task.GetAwaiter().GetResult();
    }

    private static void InvokePrivateTask(object target, string methodName, params object?[] arguments)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Private task method '{methodName}' was not found.");
        var task = method.Invoke(target, arguments) as Task
            ?? throw new InvalidOperationException($"Private method '{methodName}' did not return Task.");
        task.GetAwaiter().GetResult();
    }

    private sealed class TestDesignerHostServices : IDesignerHostServices
    {
        public TestDesignerHostServices()
        {
            Clipboard = new TestDesignerClipboard();
            Dialogs = new TestDesignerDialogService();
            FilePicker = new TestDesignerFilePickerService();
            Notifications = new TestDesignerNotificationService();
            Paths = new TestDesignerPathService();
            FileSystem = new PhysicalDesignerFileSystem();
            Scheduler = new TestDesignerScheduler();
            ExternalLauncher = new TestDesignerExternalLauncher();
            Commands = new TestDesignerHostCommandService();
        }

        public TestDesignerClipboard Clipboard { get; }
        IDesignerClipboard IDesignerHostServices.Clipboard => Clipboard;
        public TestDesignerDialogService Dialogs { get; }
        IDesignerDialogService IDesignerHostServices.Dialogs => Dialogs;
        public TestDesignerFilePickerService FilePicker { get; }
        IDesignerFilePickerService IDesignerHostServices.FilePicker => FilePicker;
        public TestDesignerNotificationService Notifications { get; }
        IDesignerNotificationService IDesignerHostServices.Notifications => Notifications;
        public TestDesignerPathService Paths { get; }
        IDesignerPathService IDesignerHostServices.Paths => Paths;
        public IDesignerFileSystem FileSystem { get; }
        public IDesignerScheduler Scheduler { get; }
        public IDesignerExternalLauncher ExternalLauncher { get; }
        public IDesignerHostCommandService Commands { get; }
    }

    private sealed class TestDesignerClipboard : IDesignerClipboard
    {
        public string? LastText { get; private set; }
        public Task<string?> GetTextAsync(CancellationToken cancellationToken = default) => Task.FromResult(LastText);
        public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            LastText = text;
            return Task.CompletedTask;
        }
    }

    private sealed class TestDesignerDialogService : IDesignerDialogService
    {
        public List<DesignerDialogRequest> Requests { get; } = new();
        public DesignerDialogResult NextResult { get; set; } = DesignerDialogResult.Cancelled;
        public Task<DesignerDialogResult> ShowAsync(DesignerDialogRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(NextResult);
        }
    }

    private sealed class TestDesignerFilePickerService : IDesignerFilePickerService
    {
        public Queue<IDesignerHostFile> OpenFiles { get; } = new();
        public List<DesignerOpenFilePickerOptions> OpenRequests { get; } = new();
        public List<DesignerSaveFilePickerOptions> SaveRequests { get; } = new();
        public string? NextFolder { get; set; }

        public Task<IReadOnlyList<IDesignerHostFile>> OpenFilesAsync(DesignerOpenFilePickerOptions options, CancellationToken cancellationToken = default)
        {
            OpenRequests.Add(options);
            return Task.FromResult<IReadOnlyList<IDesignerHostFile>>(OpenFiles.Count == 0
                ? Array.Empty<IDesignerHostFile>()
                : new[] { OpenFiles.Dequeue() });
        }

        public Task<IDesignerHostFile?> SaveFileAsync(DesignerSaveFilePickerOptions options, CancellationToken cancellationToken = default)
        {
            SaveRequests.Add(options);
            return Task.FromResult<IDesignerHostFile?>(null);
        }

        public Task<string?> SelectFolderAsync(string title, string? initialDirectory = null, CancellationToken cancellationToken = default) => Task.FromResult(NextFolder);
    }

    private sealed class TestDesignerHostFile : IDesignerHostFile
    {
        public TestDesignerHostFile(string name, string? localPath, string contents = "")
        {
            Name = name;
            LocalPath = localPath;
            _contents = Encoding.UTF8.GetBytes(contents);
        }

        private readonly byte[] _contents;
        public string Name { get; }
        public string? LocalPath { get; }
        public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream(_contents, writable: false));
        public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream());
    }

    private sealed class TestDesignerNotificationService : IDesignerNotificationService
    {
        public List<DesignerNotification> Published { get; } = new();
        public void Publish(DesignerNotification notification) => Published.Add(notification);
    }

    private sealed class TestDesignerPathService : IDesignerPathService
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "FormDesignerHostSmoke", Guid.NewGuid().ToString("N"));
        public string ApplicationBaseDirectory => _root;
        public string UserDataDirectory => Path.Combine(_root, "user-data");
        public string LogsDirectory => Path.Combine(UserDataDirectory, "logs");
        public string TempDirectory => Path.Combine(_root, "temp");
        public string PluginDirectory => Path.Combine(_root, "plugins");
        public string RecoveryDirectory => Path.Combine(UserDataDirectory, "recovery");
        public string ArtifactsDirectory => Path.Combine(_root, "artifacts");
    }

    private sealed class TestDesignerScheduler : IDesignerScheduler
    {
        public bool CheckAccess() => true;
        public void Post(Action action, DesignerSchedulerPriority priority = DesignerSchedulerPriority.Normal) => action();
        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class TestDesignerExternalLauncher : IDesignerExternalLauncher
    {
        public List<string> OpenedTargets { get; } = new();
        public Task OpenFileAsync(string path, CancellationToken cancellationToken = default) { OpenedTargets.Add(path); return Task.CompletedTask; }
        public Task OpenFolderAsync(string path, CancellationToken cancellationToken = default) { OpenedTargets.Add(path); return Task.CompletedTask; }
        public Task OpenUriAsync(Uri uri, CancellationToken cancellationToken = default) { OpenedTargets.Add(uri.AbsoluteUri); return Task.CompletedTask; }
    }

    private sealed class TestDesignerHostCommandService : IDesignerHostCommandService
    {
        public List<DesignerHostCommand> Requested { get; } = new();
        public Task ExecuteAsync(DesignerHostCommand command, CancellationToken cancellationToken = default)
        {
            Requested.Add(command);
            return Task.CompletedTask;
        }
    }

    private sealed class RuntimePreviewSmokeRow
    {
        public string Name { get; init; } = "";
    }

    private sealed class SmokePreviewContext : IPreviewContext
    {
        public DesignerPreviewMode Mode => DesignerPreviewMode.Designer;

        public IServiceProvider Services { get; } = new EmptyServiceProvider();

        public IReadOnlyList<IDesignControlNode> GetChildren(string parentId) => Array.Empty<IDesignControlNode>();

        public BindingSourceMetadata? GetBindingSource(string bindingSourceId) => null;
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class SmokeControlNode : IDesignControlNode
    {
        public SmokeControlNode(
            string id,
            string typeKey,
            string name,
            string parentId,
            string descriptorId,
            string pluginId,
            string pluginVersion,
            IReadOnlyDictionary<string, object?> builtInProperties,
            IReadOnlyDictionary<string, string> customProperties)
        {
            Id = id;
            TypeKey = typeKey;
            Name = name;
            ParentId = parentId;
            DescriptorId = descriptorId;
            PluginId = pluginId;
            PluginVersion = pluginVersion;
            BuiltInProperties = builtInProperties;
            CustomProperties = customProperties;
        }

        public string Id { get; }
        public string TypeKey { get; }
        public string Name { get; }
        public string ParentId { get; }
        public string DescriptorId { get; }
        public string PluginId { get; }
        public string PluginVersion { get; }
        public IReadOnlyDictionary<string, object?> BuiltInProperties { get; }
        public IReadOnlyDictionary<string, string> CustomProperties { get; }
    }

    private sealed class GeneratedApplicationLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public GeneratedApplicationLoadContext(string entryAssemblyPath)
            : base($"generated-app-{Guid.NewGuid():N}", isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // A generated window must share Avalonia and Eremex assemblies with the
            // headless Designer runtime. Loading a second copy would invalidate
            // Control type identity and turns this visual test into a false result.
            if (IsSharedUiAssembly(assemblyName.Name))
            {
                var loaded = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(candidate =>
                    AssemblyName.ReferenceMatchesDefinition(candidate.GetName(), assemblyName));
                if (loaded is not null)
                    return loaded;

                try
                {
                    return AssemblyLoadContext.Default.LoadFromAssemblyName(assemblyName);
                }
                catch (FileNotFoundException)
                {
                    return null;
                }
            }

            var resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
            return resolvedPath is null ? null : LoadFromAssemblyPath(resolvedPath);
        }

        private static bool IsSharedUiAssembly(string? assemblyName)
        {
            return !string.IsNullOrWhiteSpace(assemblyName)
                   && (assemblyName.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase)
                       || assemblyName.StartsWith("Eremex.", StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed class TemporaryExportProject : IDisposable
    {
        public TemporaryExportProject(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public void Dispose()
        {
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    if (Directory.Exists(Path))
                        Directory.Delete(Path, recursive: true);
                    return;
                }
                catch (IOException) when (attempt < 5)
                {
                    Thread.Sleep(250 * attempt);
                }
                catch (UnauthorizedAccessException) when (attempt < 5)
                {
                    Thread.Sleep(250 * attempt);
                }
                catch (Exception ex) when (attempt == 5)
                {
                    Debug.WriteLine($"Temporary export cleanup deferred: {Path}; reason={ex.Message}");
                    return;
                }
            }
        }
    }

    private sealed class DisposeAction : IDisposable
    {
        private readonly Action _dispose;
        private bool _isDisposed;

        public DisposeAction(Action dispose)
        {
            _dispose = dispose;
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _dispose();
        }
    }
}

