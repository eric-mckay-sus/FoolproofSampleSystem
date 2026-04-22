^XA
^DFR:FPSAMPLE304.ZPL^FS ^FX Save to the R drive
^CI28 ^FX Switch to UTF-8 for character encoding (ZPL default is Cp-850, which is archaic)

^FX for an 3x1 label at 12dpmm (304 dpi, colloquially 300 dpi), the dimensions are 914x304 ^FS

^FX --------ROW 1--------
^LH4,4^FS ^FX Sets origin to not print at the edge
^FO0,0^GB906,297,4^FS ^FX Box to the safety edge of the label
^FO0,0^GB120,70^FS ^FX Dummy sample number container (comfortably fits 4 digits)
^FO0,19^AF,26^FB120,,,C^FN1^FS ^FX Dummy sample number field
^FO120,0^GB692,70^FS ^FX Model name container (Space for 28/32 characters)
^FO120,19^AF,26^FB692,,,C^FN2^FS ^FX Model name field
^FO812,0^GB94,70^FS ^FX Severity rank container (Plenty of space for 1 character)
^FO812,19^AF,26^FB94,,,C^FN3^FS ^FX Severity field

^FX --------ROW 2--------
^LH4,74^FS ^FX Sets origin to start at row 2 (standardizes y height row-wide)
^FO0,0^GB39,49,39^FS ^FX Filled box (for ID title)
^FO0,15^A0,24^FB39,,,C^FR^FDID\&^FS ^FX Centered text in filled box
^FO39,0^GB192,49^FS ^FX Dummy sample serial container (Space for 7/10 digits)
^FO39,10^AF,26^FB192,,,C^FN4^FS ^FX Dummy sample serial field
^FO231,0^GB234,49^FS ^FX Assembly line container (Space for 9 characters, 2 more than current max)
^FO231,10^AF,26^FB234,,,C^FN5^FS ^FX Assembly line field
^FO464,0^GB60,,49^FS ^FX Filled box (for REV title)
^FO464,15^A0,24^FB60,,,C^FR^FDREV\&^FS ^FX Centered text in filled box
^FO524,0^GB112,49^FS ^FX Iteration number container (Space for 4 digits, 2 more than current max)
^FO524,10^AF,26^FB112,,,C^FN6^FS ^FX Iteration number field
^FO636,0^GB267,49^FS ^FX Creation date container (Fits full date with space)
^FO636,10^AF,26^FB267,,,C^FN7^FS ^FX Creation date field

^FX --------ROW 3--------
^LH4,123^FS ^FX Sets origin to start at row 3 (standardizes y height row-wide)
^FO0,0^GB675,126^FS ^FX Process failure mode container (space for all 100 characters)
^FO0,15^AD,18^FB675,4,7,C^FN8^FS ^FX Process failure mode field
^FO675,0^GB231,126^FS ^FX Location container (space for all 32 characters)
^FO675,15^AD,18^FB231,4,7,C^FN9^FS ^FX Location field

^FX --------ROW 4--------
^LH4,249^FS ^FX Sets origin to start at row 4 (standardizes y height row-wide)
^FO0,0^GB135,,49^FS ^FX Filled box (for MAKER title)
^FO0,15^A0,24^FB135,,,C^FR^FDMAKER\&^FS ^FX Centered text in filled box
^FO135,0^GB316,49^FS ^FX Creator container (plenty of space for associate number
^FO135,7^AF,28^FB316,,,C^FN10^FS ^FX Creator field
^FO451,0^GB145,,49^FS ^FX Filled box (for APPROVAL title)
^FO451,15^A0,24^FB145,,,C^FR^FDAPPROVAL\&^FS ^FX Centered text in filled box
^FO596,0^GB310,49^FS ^FX Approver container (plenty of space for associate number
^FO596,7^AF,28^FB310,,,C^FN11^FS ^FX Approver field (likely never used)

^XZ

^XA
^XFR:FPSAMPLE304.ZPL ^FX Load the template
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
