function getColor(i, max_i)
    if i >= max_i then return 0, 0, 0, 1 end

    local zn = math.sqrt(zx * zx + zy * zy)
    local smooth_i = i + 1 - math.log(math.log(zn)) / math.log(2)

    local cycle = i / smooth_i

    local r = 0.5 * math.sin(2 * math.pi * cycle + math.pi)
    local g = 0.5 * math.sin(2 * math.pi * cycle + 2 * math.pi / 3)
    local b = 0.5 * math.sin(2 * math.pi * cycle + 4 * math.pi / 3)

    return r, g, b, 1
end

function fractal(zoom, translate_x, translate_y, precision)
    cnvs = nil
    cnvs = love.graphics.newCanvas(1000, 1000)
    love.graphics.setCanvas(cnvs)
    max_i = precision
    for y = 0, love.graphics.getHeight(), 1 do
        for x = 0, love.graphics.getWidth(), 1 do
            zx = 0
            zy = 0
            real = (x - 500) / zoom + translate_x
            imag = (y - 500) / zoom + translate_y
            i = 0
            while zx * zx + zy * zy <= 4 and i <= max_i do
                zx_new = (zx * zx - zy * zy) + real
                zy_new = (2 * zx * zy) + imag
                zx = zx_new
                zy = zy_new
                i = i + 1
            end
            if i < max_i / 2 then
                color = {}
                color = { getColor(i, max_i) }
                love.graphics.setColor(color)
                love.graphics.points(x, y)
                love.graphics.setColor(1, 1, 1, 1)
            end
        end
    end
    love.graphics.setCanvas()
end
