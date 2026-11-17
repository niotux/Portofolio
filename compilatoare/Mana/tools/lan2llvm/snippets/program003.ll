  %a.0 = add i32 %0, 0
  %b.0 = add i32 %1, 0
  %p.0 = inttoptr i64 0 to ptr
  %r.0 = add i32 %a.0, %b.0
  %"<f>fr.0" = sitofp i32 %r.0 to double
  %"<f>fa.0" = sitofp i32 %a.0 to double
  %"<f>fb.0" = sitofp i32 %b.0 to double
  %"<f>fm.0" = fmul double %"<f>fa.0", %"<f>fb.0"
  br label %Loop
Loop:
  %a.1 = phi i32 [ %a.0, %entry ], [ %a.1, %Loop ]
  %b.1 = phi i32 [ %b.0, %entry ], [ %b.1, %Loop ]
  %p.1 = phi ptr [ %p.0, %entry ], [ %p.1, %Loop ]
  %r.1 = phi i32 [ %r.0, %entry ], [ %r.1, %Loop ]
  %"<f>fr.1" = phi double [ %"<f>fr.0", %entry ], [ %"<f>fr.1", %Loop ]
  %"<f>fa.1" = phi double [ %"<f>fa.0", %entry ], [ %"<f>fa.1", %Loop ]
  %"<f>fb.1" = phi double [ %"<f>fb.0", %entry ], [ %"<f>fb.1", %Loop ]
  %"<f>fm.1" = phi double [ %"<f>fm.0", %entry ], [ %"<f>fm.2", %Loop ]
  %"<f>fm.2" = fdiv double %"<f>fm.2", %"<f>fr.1"
  %fjcmp.c = fcmp ogt double %"<f>fm.2", 2.0
  br i1 %fjcmp.c, label %Loop, label %Loop.after
P„Ì ü:
  ret double %"<f>fm.2"
