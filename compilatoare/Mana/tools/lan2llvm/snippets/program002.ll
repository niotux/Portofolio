  %a = add i32 %0, 0
  %b = add i32 %1, 0
  %p = inttoptr i64 0 to ptr
  %r = add i32 %a, %b
  %<f>fr = sitofp i32 %r to double
  %<f>fa = sitofp i32 %a to double
  %<f>fb = sitofp i32 %b to double
  %<f>fm = fmul double %<f>fa, %<f>fb
Loop:
  %<f>fm = fdiv double %<f>fm, %<f>fr
  %fjcmp.c = fcmp ogt double %<f>fm, 2.0
  br i1 %fjcmp.c, label %Loop, label %Loop.after
Loop.after:
  ret i64 %fm
