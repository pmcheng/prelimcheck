# PrelimCheck  

**PrelimCheck is a Windows program to facilitate follow-up of preliminary notes placed in the Synapse PACS.**

Radiology residents who take call routinely place preliminary notes into the PACS describing their findings and impressions for cases that they review.  However, because of the volume of cases reviewed during a call shift, it is often difficult to follow up on the final attending reads on all of these cases.  This program is designed to facilitate follow-up of preliminary notes as a routine educational post-call activity.

Instructions
------------

1. Select the start time and the duration (in hours) of the call shift.
2. Select the location:
    - County - must be on-site at LACUSC.
    - Keck/Norris, select ***local*** if on-site at Keck or Norris, otherwise select ***remote***.
3. Click ***Download***.  This may take a minute. **PrelimCheck** will search for notes created in the time interval, and retrieve the corresponding chain of notes and reports.  
4. Click any row of the table and the notes appear on the left panel, and the report (if available) on the right panel.  If the report contains an impression, the report pane will automatically scroll to the impression.  Use up and down arrow keys or page-up/page-down to scroll through the studies.  The study data can be sorted by any column by clicking on the column header.  
5. Enter text to search notes/reports into the ***Filter Text*** field.  Multiple search terms can be separated by spaces, and phrases can be searched by entering them within quotes.  For instance, the search phrase ***p111999 "appendiceal rupture"*** will display rows whose notes or reports contain both *p111999* and the phrase *appendiceal rupture*.  Note that phrase searches will not detect phrases split across two lines of a report.
6. Click ***Save*** to save the currently displayed table as a CSV file which can be reloaded into **PrelimCheck** (select ***Open***) or loaded into spreadsheet software such as Excel.  Patient names are not saved.  

Requires .NET Framework 2.0 or higher.

This project uses code from the following open source projects:

* [Fast CSV Reader](http://www.codeproject.com/Articles/9258/A-Fast-CSV-Reader)
* [HTML Agility Pack](http://htmlagilitypack.codeplex.com)
