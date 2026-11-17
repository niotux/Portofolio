function mapdisplay(type, xqvd, yqvd)
    local imageH, imageV

    cnvs = love.graphics.newCanvas(Hres, Vres)
    love.graphics.setCanvas(cnvs)

    if type == "galaxy" then
        love.graphics.setColor(1, 1, 1)
        imageH = backgroundIMG:getWidth()
        imageV = backgroundIMG:getHeight()
        love.graphics.draw(backgroundIMG, 0 - Hres * xqvd, 0 - Hres * yqvd, 0, 2 * Hres / imageH, 2 * Vres / imageV)
        love.graphics.setColor(0, 0.5, 1)
        for yi = 0, ymap do
            for xi = 0, xmap do
                love.graphics.rectangle("line", xi * (Hres / xmap), yi * (Vres / ymap), (Hres / xmap) - 1,
                    (Vres / ymap) - 1)
            end
        end
    end

    love.graphics.setCanvas()
end
