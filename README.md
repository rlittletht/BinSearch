# BinSearch
Implements a binary search of a sorted text file. 

Usage: `dotnet binseearch.dll <searchprefix> <searchfile.txt>`

The search prefix will be searched for in searchfile.txt. The match must match the beginning of the line, and (for now) is assumed to be case
insensitive. Since the text file is assumed to be sorted, a binary search will be performed.

The input file does not need to have uniform line lengths, nor does it have to have uniform line endings (though adjacent line endings 
must be equivalent..
