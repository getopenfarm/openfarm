# File estimation and printer credentials

Slicing defaults selects the farm's slicer engine and version. Estimation profiles
are no longer configured in Settings. Sliced 3MF and G-code use embedded metadata;
unsliced project 3MF uses embedded settings and its named project presets when
supported. STL and geometry-only 3MF remain reviewable and saveable without
estimates. Missing values remain unknown rather than zero.

Existing part estimates and processing snapshots are preserved. Reprocessing
resolves the file again using the current slicer selection.

Enter connection credentials when adding or editing a printer. Credentials remain
stored encrypted for printer connections. Standalone credential list/create APIs
and the Connection credentials settings pages have been removed.

The RemoveEstimationDefaults migration drops only the four farm estimation-default
columns. It does not delete printer credentials or part estimates.
