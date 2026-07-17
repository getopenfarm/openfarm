# 002-printer-integration
1. Allow edit all printer info except brand, modelCode and serial number. Only those display purpose or connection purpose field are allow to change. So we need to remove PUT /api/v1/printers/{id}/connection endpoint and maybe replace with a patch?


# 004-virtual-printer
1. Since we're going to restructure, is it we need restructure or revisit signal r farm hub too? We might have to change or improve the PrinterTelemetryUpdated signal or other signal to prepare for other brand or other model printer too.
2. Please revisit printer contract and data model.
3. How current UI populates the printer brand and models, can we pull the printers catalog from backend? Then we no need maintain extra catalog at frontend.