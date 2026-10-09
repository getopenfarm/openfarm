# Bambu alert reference data

`hms_en.json.gz` and `wiki_links.json.gz` are copied unchanged from the repository's existing ha-bambulab reference checkout:

- Repository: https://github.com/greghesp/ha-bambulab
- Commit: `f7e52909949822b76bcd9b7c95cd55c421510aa8`
- Directory: `custom_components/bambu_lab/pybambu/hms_error_text/`

The text files contain Bambu HMS and print-error descriptions and model-specific troubleshooting links. OpenFarm embeds the English data for offline lookup; these copies let ordinary backend builds work without initializing nested reference submodules. To update, replace both files from a reviewed reference revision and run `BambuAlertParserTests`.
