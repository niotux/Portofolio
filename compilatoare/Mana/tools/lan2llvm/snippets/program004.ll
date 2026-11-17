%a.Integer.0 = add i64 0, 10
%b.Integer.0 = add i64 0, 20
%res.Integer.0 = add i64 %a.Integer.0, %b.Integer.0
%a.Integer.1 = sub i64 %a.Integer.0, %b.Integer.0
%a.Float.0 = bitcast i64 %a.Integer.1 to double
%b.Float.0 = bitcast i64 %b.Integer.0 to double
%fa.Float.0 = fadd double %a.Float.0, %b.Float.0
