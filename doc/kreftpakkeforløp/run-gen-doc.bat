REM Generer HTML og PDF fra asciidoc

echo off

set docname=%1
if not defined docname (set docname=kreftforlop_dokumentasjon)
set adoc=%docname%.adoc
set styledoc=dips.css
set adpdf=C:\Temp\asciidoctor-pdf\bin\asciidoctor-pdf
set fontdir=C:\Temp\fonts


call asciidoctor -a stylesheet=%styledoc% -a toc-left %adoc%
call ruby %adpdf% -a pdf-fontsdir=%fontdir% %adoc%

rem asciidoctor-pdf user.adoc && asciidoctor user.adoc