// 与旧 Client 工程的 ImplicitUsings 等价：System.Drawing/Windows.Forms 的
// System.Drawing 类型由 MirMath 替换（Point 等），其余保持 BCL 隐式 using。
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Net.Http;
global using System.Threading;
global using System.Threading.Tasks;
global using Point = Crystal.Client.Core.MirMath.Point;
global using Color = Crystal.Client.Core.MirMath.Color;
global using Font = Crystal.Client.Core.MirMath.Font;
global using FontStyle = Crystal.Client.Core.MirMath.FontStyle;
