EXAMPLE TARGET LISTS
====================

Three ready-to-use CSV files you can load straight into the Speckle
plugin, plus this guide. Copy one, replace the rows with your own
targets, and you have a working list.

All data in these files is EXAMPLE data: the designations, Gaia source
ids, magnitudes, separations and position angles are plausible but are
placeholders. Replace them with real catalogue values before observing.


The files
---------
example_minimal.csv
  Three double stars, essential columns only:
  Proj, Obs, Type, Name1, Name2, RA2000, Dec2000, Pmag, Smag, Sep,
  GetRef. This is the smallest list that does something useful:
  everything not in the file falls back to the container fields and the
  plugin options. Start here.

example_full.csv
  Five program targets plus one reference-star row, using the columns
  most programs actually need: the minimal set plus Priority, Template,
  Bp, Rp, Gmag, GaiaNum, PA, Exp, NExp, RefGaiaNum, Nights, Cycles.
  It shows three variations:
    - row 1 (20467+1607): fully specified, own template, 2 cycles.
    - row 4 (20144+2016): GetRef 0 plus RefGaiaNum, so no automatic
      search is done and exactly the linked reference star is used.
      The linked star is row 5, a Type R row with the matching GaiaNum.
    - row 6 (22287+5828): GetRef 0 and no RefGaiaNum, so this target
      is observed without a reference star at all.

example_reference_stars.csv
  A small curated reference-star list, one vetted single star for each
  target in example_full.csv. This is NOT a target list: it belongs in
  Options > Plugins > SpeckleInterferometry > ReferenceStars, under
  "Choose reference star list", with "Use referencestarlist" enabled.
  The plugin then draws reference candidates from it in addition to (or
  instead of) SIMBAD and the bundled USNO list.


How to load a target list
-------------------------
1. Advanced Sequencer > drag a "Speckle Target List Container" into
   the target area of your sequence.
2. Check the container defaults it picked up from the plugin options
   (User, Template, TemplateRef, Cycles, Exposures, ExposureTime).
3. Press "Load target set" and pick the CSV.
The plugin computes tonight's visibility for every row, applies the
magnitude / separation / altitude / moon limits, gives each target its
best imaging time and sorts the plan. Rows that were skipped stay
visible with the reason in their Note2 column. Tick "Ignore limits"
before loading if you want every row kept regardless.

How to load the reference-star list
-----------------------------------
Options > Plugins > SpeckleInterferometry > ReferenceStars >
"Choose reference star list", select example_reference_stars.csv, and
leave "Use referencestarlist" on. The list is read once and cached; it
is also what a RefGaiaNum link can point into.


Format rules
------------
- Comma (,) separates columns. One header row, one target per row.
- Dot (.) is the decimal separator - always, in every locale. The file
  is read with invariant culture, so "8.31" is correct and "8,31"
  is not.
- RA2000 and Dec2000 are DECIMAL DEGREES. RA 20h 46.7m is 311.6750, not
  20:46:42 and not 20.7783. Dec +16d 07' is 16.1167. Negative
  declinations are written with a leading minus, e.g. -12.4500.
- Plain ASCII text. No degree signs, no accented characters, no quotes
  needed unless a value itself contains a comma.
- Column order does not matter, and any column may be left out - a
  missing column simply takes its default.
- A row whose RA2000 and Dec2000 are both 0 is skipped.


Required columns
----------------
RA2000 and Dec2000. That is all. Everything else is optional and has a
default. In practice you also want Name1, Pmag/Smag and Sep, because
the plugin uses them for file names and for its limit checks.


The columns used in these examples
----------------------------------
Proj        Project code. Goes into the FITS headers and the
            $$PROJECT$$ file pattern.
Obs         Observer code. Empty falls back to the container's User
            field, then to the plugin option "Default User".
Type        Row type. M = main program double star (the normal case),
            C = calibration pair (treated like M), G = galaxy or other
            fill-in (scheduled, but never gets a reference star),
            R = reference star row (not scheduled itself; it exists so
            a RefGaiaNum link has something to point at).
Name1       Primary designation, e.g. the WDS identifier. Used in file
            names and headers.
Name2       Secondary designation, e.g. the discoverer code. The target
            name becomes "Name1_Name2".
Priority    Tie-breaker when several targets want the same time slot.
            Higher goes first.
Template    Name of the saved Speckle Target Container template for
            this target. It must match a saved template name EXACTLY
            or the target is skipped at run time. Leave it empty to use
            the container's Template / the plugin's Default Template -
            that is the safe choice.
RA2000      J2000 right ascension in decimal degrees.
Dec2000     J2000 declination in decimal degrees.
Bp, Rp      Gaia Bp and Rp magnitudes. Bp - Rp is the target colour
            used when matching a reference star, and Rp sets the bright
            end of the reference search.
Gmag        Gaia G magnitude, used for brightness matching of reference
            stars.
GaiaNum     Gaia source id. Used for RefGaiaNum links, for de-duplication
            and for the $$GAIANR$$ file pattern.
Sep         Pair separation in arcseconds. Rows with Sep outside the
            plugin's Min/Max separation limits are skipped.
PA          Position angle of the pair, degrees (informational).
Pmag        Primary magnitude. The plugin's magnitude limits are checked
            against this for M and C rows.
Smag        Secondary magnitude. If it is 0 on an M or C row, the ASD
            exposure calculation is switched off for that row.
Exp         Exposure time per frame in seconds. 0 or empty uses the
            container's ExposureTime.
NExp        Number of frames per run. 0 or empty uses the container's
            Exposures.
GetRef      1 = search for a reference star automatically (default),
            0 = do not search. A RefGaiaNum link is still used.
RefGaiaNum  Manual reference-star link: the GaiaNum of another row in
            the same list, or of an entry in your reference-star list.
            Always honoured, even when GetRef is 0.
Nights      How many nights this target should be observed.
Cycles      Runs per night.

Other columns the plugin accepts but the examples leave out: Parallax,
Spectrum, GPrime, RPrime, IPrime, ZPrime, RUWE, FDBL, NoEC, Note1,
Note2, AirmassMin (also spelled Airmass), AirmassMax, MinAltitude, and
the progress counters Completed_cycles, Completed_ref_cycles and
Completed_nights. Reference-star lists additionally accept Filter,
targetrecno and recno. The complete table, with defaults and accepted
alias spellings, is in Documentation\03_TargetLists.txt.

Note that a TemplateRef column is NOT read from a target list. The
reference template comes from the container's TemplateRef field, the
plugin option "Default Reference Template", or the Template of the
linked reference row.


Continuing a program the next night
-----------------------------------
While a list runs, the plugin writes the whole list back, with updated
Completed_cycles and Completed_nights counters, to

  <your image file path>\TargetList-<yyyy-MM-dd>.csv

(the date is "now minus 12 hours", so one file covers one night across
midnight). Load that file the next night and finished targets are
skipped automatically. Keep the Completed_* columns when you edit such
a file by hand.
