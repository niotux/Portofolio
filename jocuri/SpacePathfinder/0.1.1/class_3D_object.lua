--CLASS: 3D_object
---@param vtx_table table
---@param fac_table table
---@param translate table
---@param scale number
---@param rotate table
function new_object(self, vtx_table, fac_table, translate, scale, rotate)
    self.vtx_table = vtx_table
    self.fac_table = fac_table
    self.translate = translate
    self.scale = scale
    self.rotate = rotate
    self.vtx_trans = {}

    function self.update()
        --Local variables
        local cx = math.cos(self.rotate.x)
        local sx = math.sin(self.rotate.x)
        local cz = math.cos(self.rotate.z)
        local sz = math.sin(self.rotate.z)
        local x, y, z = 0, 0, 0


        --Transformations are applied to vertices (vtx_table is transformed in vtx_trans)
        for i = 1, #self.vtx_table do
            self.vtx_trans[i] = {}
            self.vtx_trans[i].x = (self.vtx_table[i].x * self.scale + self.translate.x)
            self.vtx_trans[i].y = -(self.vtx_table[i].y * self.scale + self.translate.y)
            self.vtx_trans[i].z = (self.vtx_table[i].z * self.scale + self.translate.z)

            x = self.vtx_trans[i].x
            z = self.vtx_trans[i].z

            self.vtx_trans[i].x = cz * x + sz * z
            self.vtx_trans[i].z = -sz * x + cz * z

            y = self.vtx_trans[i].y
            z = self.vtx_trans[i].z

            self.vtx_trans[i].y = cx * y + sx * z
            self.vtx_trans[i].z = -sx * y + cx * z
        end

        --Drawing the faces
        for i = 1, #self.fac_table do
            if self.vtx_trans[self.fac_table[i][1]].z >= 0 and self.vtx_trans[self.fac_table[i][2]].z >= 0 and self.vtx_trans[self.fac_table[i][3]].z >= 0 then
                love.graphics.setColor(1, 1, 1, 1)
                love.graphics.polygon("line",
                    self.vtx_trans[self.fac_table[i][1]].x * height / 2 / (self.vtx_trans[self.fac_table[i][1]].z) +
                    width / 2,
                    self.vtx_trans[self.fac_table[i][1]].y * height / 2 / (self.vtx_trans[self.fac_table[i][1]].z) +
                    height / 2,
                    self.vtx_trans[self.fac_table[i][2]].x * height / 2 / (self.vtx_trans[self.fac_table[i][2]].z) +
                    width / 2,
                    self.vtx_trans[self.fac_table[i][2]].y * height / 2 / (self.vtx_trans[self.fac_table[i][2]].z) +
                    height / 2,
                    self.vtx_trans[self.fac_table[i][3]].x * height / 2 / (self.vtx_trans[self.fac_table[i][3]].z) +
                    width / 2,
                    self.vtx_trans[self.fac_table[i][3]].y * height / 2 / (self.vtx_trans[self.fac_table[i][3]].z) +
                    height / 2
                )
            end
        end
    end
end
