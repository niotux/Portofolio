function new_object(typ, x, y, m)
    local self = {
        typ = typ,
        x = x,
        y = y,
        m = m,
        xv = 0,
        yv = 0
    }
    function self.draw()
        self.x = self.x + self.xv * 0.01
        self.y = self.y + self.yv * 0.01
        if self.typ == 1 then
            love.graphics.setColor(1, 1, 0)
        end
        if self.typ == 2 then
            love.graphics.setColor(1, 0.5, 0)
        end
        if self.typ == 3 then
            love.graphics.setColor(1, 0, 0)
        end
        if self.typ == 4 then
            love.graphics.setColor(0, 1, 0)
        end
        if self.typ == 5 then
            love.graphics.setColor(0, 0, 1)
        end
        if self.typ == 6 then
            love.graphics.setColor(1, 1, 1)
        end
        love.graphics.circle("fill", self.x / 2, self.y / 2, 2.5)
        love.graphics.setColor(1, 1, 1)
    end

    return self
end
