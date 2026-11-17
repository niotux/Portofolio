---@param x integer
---@param y integer
---@param xsec integer
---@param ysec integer
---@param obj tablelib
function new_system(self, x, y, xsec, ysec, obj)
    self.x = x
    self.y = y
    self.xsec = xsec
    self.ysec = ysec
    self.obj = obj
    function self.draw()
        --IMAGE GET_RES
        local imageW = shipIMG:getWidth()
        local imageH = shipIMG:getHeight()

        --PIXEL COORDS
        local xPIXEL = unit * self.x
        local yPIXEL = unit * self.y
        if mapKey then
            love.graphics.draw(mapF1IMG, (xPIXEL + self.xsec * minRES) / xmap + wCrct - imageW * unit / 2,
                (yPIXEL + self.ysec * minRES) / ymap + hCrct - imageH * unit, 0,
                unit, unit)
        else
            if player_ship.xsec == self.xsec and player_ship.ysec == self.ysec then
                love.graphics.circle("line", xPIXEL - imageW * unit + wCrct,
                    yPIXEL - imageH * unit + hCrct, 100)
            end
        end
    end
end
