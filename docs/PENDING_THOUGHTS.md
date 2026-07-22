# 002-printer-integration
1. Allow edit all printer info except brand, modelCode and serial number. Only those display purpose or connection purpose field are allow to change. So we need to remove PUT /api/v1/printers/{id}/connection endpoint and maybe replace with a patch?
2. Edit and remove printer should be in detail page. Table should now allow edit printer or remove.

# 004-virtual-printer
1. Since we're going to restructure, is it we need restructure or revisit signal r farm hub too? We might have to change or improve the PrinterTelemetryUpdated signal or other signal to prepare for other brand or other model printer too.
2. How current UI populates the printer brand and models, can we pull the printers catalog from backend? Then we no need maintain extra catalog at frontend.

# 009-process-profiles
1. STL uploads default to a stale hardcoded "Generic PLA"/"Bambu PLA Basic" fallback (`SlicerOptions.GenericFallbackMaterialProfile` in api/src/OpenFarm.Infrastructure/Parts/SlicerOptions.cs) that no longer matches `FilamentProfile.MaterialType`'s material-category contract (e.g. "PLA") introduced by the 009 rework. Result: slicing silently fails for OrcaSlicer/Bambu Studio (`SlicingProfileUnconfiguredException`, only visible via Activity Log `PartEstimateUnavailable`), and SuperSlicer passes the bad literal straight through as an unvalidated `--filament-type` CLI flag. Fix: change the fallback values to material-category strings (e.g. "PLA") to match `RepresentativeFilamentResolver`'s matching semantics.