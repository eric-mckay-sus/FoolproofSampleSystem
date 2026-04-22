^XA
^DFR:FPSAMPLE203.ZPL^FS ^FX Save to the R drive
^CI28 ^FX Switch to UTF-8 for character encoding (ZPL default is Cp-850, which is archaic)

^FX for an 3x1 label at 8dpmm (203 dpi), the dimensions are 609x203 ^FS

^FX --------ROW 1--------
^LH3,3^FS ^FX Sets origin to not print at the edge
^FO0,0^GB603,197,3^FS ^FX Box to the safety edge of the label
^FO0,0^GB80,47^FS ^FX Dummy sample number container (comfortably fits 4 digits)
^FO0,13^AF,26^FB80,,,C^FN1^FS ^FX Dummy sample number field
^FO80,0^GB462,47^FS ^FX Model name container (Space for 28/32 characters)
^FO80,13^AF,26^FB462,,,C^FN2^FS ^FX Model name field
^FO542,0^GB60,47^FS ^FX Severity rank container (Plenty of space for 1 character)
^FO542,13^AF,26^FB60,,,C^FN3^FS ^FX Severity field

^FX --------ROW 2--------
^LH3,50^FS ^FX Sets origin to start at row 2 (standardizes y height row-wide)
^FO0,0^GB26,33,26^FS ^FX Filled box (for ID title)
^FO0,10^A0,16^FB26,,,C^FR^FDID\&^FS ^FX Centered text in filled box
^FO26,0^GB128,33^FS ^FX Dummy sample serial container (Space for 7/10 digits)
^FO26,7^AF,26^FB128,,,C^FN4^FS ^FX Dummy sample serial field
^FO154,0^GB156,33^FS ^FX Assembly line container (Space for 9 characters, 2 more than current max)
^FO154,7^AF,26^FB156,,,C^FN5^FS ^FX Assembly line field
^FO310,0^GB40,,33^FS ^FX Filled box (for REV title)
^FO310,10^A0,16^FB40,,,C^FR^FDREV\&^FS ^FX Centered text in filled box
^FO350,0^GB75,33^FS ^FX Iteration number container (Space for 4 digits, 2 more than current max)
^FO350,7^AF,26^FB75,,,C^FN6^FS ^FX Iteration number field
^FO425,0^GB178,33^FS ^FX Creation date container (Fits full date with space)
^FO425,7^AF,26^FB178,,,C^FN7^FS ^FX Creation date field

^FX --------ROW 3--------
^LH3,83^FS ^FX Sets origin to start at row 3 (standardizes y height row-wide)
^FO0,0^GB451,83^FS ^FX Process failure mode container (space for all 100 characters)
^FO0,10^AD,17^FB451,3,5,C^FN8^FS ^FX Process failure mode field
^FO451,0^GB150,83^FS ^FX Location container (space for all 32 characters)
^FO451,10^AD,17^FB150,3,5,C^FN9^FS ^FX Location field

^FX --------ROW 4--------
^LH3,166^FS ^FX Sets origin to start at row 4 (standardizes y height row-wide)
^FO0,0^GB90,,33^FS ^FX Filled box (for MAKER title)
^FO0,10^A0,16^FB90,,,C^FR^FDMAKER\&^FS ^FX Centered text in filled box
^FO90,0^GB211,33^FS ^FX Creator container (plenty of space for associate number
^FO90,5^AF,28^FB211,,,C^FN10^FS ^FX Creator field
^FO301,0^GB97,,33^FS ^FX Filled box (for APPROVAL title)
^FO301,10^A0,16^FB97,,,C^FR^FDAPPROVAL\&^FS ^FX Centered text in filled box
^FO398,0^GB203,33^FS ^FX Approver container (plenty of space for associate number
^FO398,5^AF,28^FB203,,,C^FN11^FS ^FX Approver field (likely never used)

^XZ

^XA
^XFR:FPSAMPLE203.ZPL ^FX Load the template
^FN1^FD0000\&^FS
^FN2^FDASDFJKL;ASDFJKL;ASDFJKL;ASDFJKL;\&^FS
^FN3^FDG\&^FS
^FN4^FD1234567\&^FS
^FN5^FDEL2C13\&^FS
^FN6^FD32\&^FS
^FN7^FD02/27/2014\&^FS
^FN8^FDTHIS SEEMS LIKE A REASONABLE LENGTH FOR A PROCESS FAILURE MODE\&^FS
^FN9^FDIN CIRCUIT TEST MC\&^FS
^FN10^FD9548\&^FS
^XZ
