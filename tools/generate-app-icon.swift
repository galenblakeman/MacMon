import AppKit

// WinMon docs/design/icon/WinMon.xaml, MIT license: dependencies/winmon-ui/LICENSE.
// Render each macOS size from the original geometry, including the small-size stroke correction.
let output = CommandLine.arguments.dropFirst().first ?? "artifacts/MacMon.iconset"
try FileManager.default.createDirectory(atPath: output, withIntermediateDirectories: true)
let frames = [("icon_16x16",16), ("icon_16x16@2x",32), ("icon_32x32",32), ("icon_32x32@2x",64),
              ("icon_128x128",128), ("icon_128x128@2x",256), ("icon_256x256",256), ("icon_256x256@2x",512),
              ("icon_512x512",512), ("icon_512x512@2x",1024)]
for (name, size) in frames {
    let space = CGColorSpaceCreateDeviceRGB()
    let context = CGContext(data: nil, width: size, height: size, bitsPerComponent: 8, bytesPerRow: size * 4,
                            space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    context.translateBy(x: 0, y: CGFloat(size))
    context.scaleBy(x: CGFloat(size) / 256, y: -CGFloat(size) / 256)
    let tile = CGMutablePath()
    tile.move(to: CGPoint(x:48,y:8)); tile.addLine(to: CGPoint(x:208,y:8))
    tile.addQuadCurve(to: CGPoint(x:248,y:48), control: CGPoint(x:248,y:8))
    tile.addLine(to: CGPoint(x:248,y:208)); tile.addQuadCurve(to: CGPoint(x:208,y:248), control: CGPoint(x:248,y:248))
    tile.addLine(to: CGPoint(x:48,y:248)); tile.addQuadCurve(to: CGPoint(x:8,y:208), control: CGPoint(x:8,y:248))
    tile.addLine(to: CGPoint(x:8,y:48)); tile.addQuadCurve(to: CGPoint(x:48,y:8), control: CGPoint(x:8,y:8)); tile.closeSubpath()
    func color(_ r: CGFloat, _ g: CGFloat, _ b: CGFloat) -> CGColor {
        CGColor(colorSpace: space, components: [r/255,g/255,b/255,1])!
    }
    context.saveGState(); context.addPath(tile); context.clip()
    let gradient = CGGradient(colorsSpace: space, colors: [color(44,51,60),color(18,23,28)] as CFArray, locations: [0,1])!
    context.drawLinearGradient(gradient, start: CGPoint(x:0,y:8), end: CGPoint(x:0,y:248), options: [])
    context.restoreGState()
    context.addPath(tile); context.setStrokeColor(color(65,75,86)); context.setLineWidth(2); context.strokePath()
    let points = [(30,151),(65,151),(82,106),(108,188),(132,132),(156,188),(188,74),(211,151),(226,151)]
    context.move(to: CGPoint(x:points[0].0,y:points[0].1))
    for p in points.dropFirst() { context.addLine(to: CGPoint(x:p.0,y:p.1)) }
    context.setStrokeColor(color(83,245,106)); context.setLineWidth(size <= 24 ? 20 : 14)
    context.setLineCap(.round); context.setLineJoin(.round); context.strokePath()
    let bitmap = NSBitmapImageRep(cgImage: context.makeImage()!)
    try bitmap.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: output).appendingPathComponent(name + ".png"))
}
