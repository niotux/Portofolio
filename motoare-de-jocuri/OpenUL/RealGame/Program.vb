option strict on
option infer on
option explicit off
option compare binary

imports System

public module Program
  public sub Main()
    FileOpen(1, "/dev/stdout", OpenMode.Output)
    PrintLine(1, "Hello")
    dim x = #12/3/2020#
    dim col = new Collection
    dim a% : dim b# : dim c$ : dim d! : dim e@ : dim f&
    dim arr$()
    redim arr$(30)
    for i = 1 to arr.Length step 2

    next i
    col.Add(10) : col.Add(x, cstr(x))
    erase arr
  end sub
end module