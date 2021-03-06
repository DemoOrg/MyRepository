﻿module RayTracer
    open Shape
    open Ray
    open Point
    open Vector
    open Material
    open Light
    open Scene
    open Color

    type Intersection = { At: Point3; Normal: Vector3; Material: Material; Illumination: Color }

    type IlluminationTree =
        | NoIllumination
        | IlluminationSource of Intersection * IlluminationTree * IlluminationTree


    let colors = Color.ByName
    let black = colors.["Black"]

    let rec CalculateTotalIllumination (illuminationTree: IlluminationTree) =
        match illuminationTree with
        | NoIllumination -> black
        | IlluminationSource(hit, reflected, refracted ) -> 
                                let percentFromRefraction = 1. - hit.Material.Reflectivity
                                hit.Illumination 
                                + hit.Material.Reflectivity * ( CalculateTotalIllumination reflected ) 
                                + percentFromRefraction * (CalculateTotalIllumination refracted )

    let rec CalculateTotalIlluminationTail cont t =
        match t with
        | NoIllumination -> cont black
        | IlluminationSource(hit,reflected,refracted) -> 
            let percentFromRefraction = 1. - hit.Material.Reflectivity
            let percentFromReflection = hit.Material.Reflectivity
            let fromLight = hit.Illumination

            let f = fun (right:Color) -> CalculateTotalIlluminationTail ( fun left -> cont (fromLight + percentFromReflection * left + percentFromRefraction * right) ) reflected
            CalculateTotalIlluminationTail f refracted    

    let FindIntersections shapes ( ray: Ray ) =
        shapes   |> List.map( fun s -> (s.Intersection ray) ) 
                |> List.filter ( 
                    fun h -> match h with
                                | None -> true
                                | Some(time,_,_,_,_) -> 0. < time )

    let FindNearestIntersection shapes (ray:Ray) =
        FindIntersections shapes ray |> List.reduce (  
                                            fun acc intersection -> 
                                                match acc with
                                                | None -> intersection
                                                | Some(time, _, _, _,_) ->
                                                    match intersection with
                                                    | Some(intersectionTime, _, _, _,_) when intersectionTime < time
                                                        -> intersection
                                                    | _ -> acc )

    let CalculateLightIllumination material point (normal: Vector3) (eyeDirection: Vector3) shapes light =
        let surfaceToLight = ( light.Position - point ).Normalize()
        let surfaceToLightRay = new Ray( point + surfaceToLight * 0.0001, surfaceToLight )

        match FindNearestIntersection shapes surfaceToLightRay with
        | None -> CalculateLightIllumination material eyeDirection surfaceToLight normal light
        | _ -> black

    let IlluminationFromAllLights scene material point (normal: Vector3) (ray: Ray) =
        let CalculateLightIlluminationAtThisPoint = CalculateLightIllumination material point normal -ray.Direction
        scene.Lights |> List.map ( fun light -> CalculateLightIlluminationAtThisPoint scene.Shapes light )
                     |> List.reduce ( fun acc color -> acc + color )

    let rec BuildLightRayTree scene numberOfReflections ray =
        let FindNearestHitInScene = FindNearestIntersection scene.Shapes
        let hit = if numberOfReflections <= 0 then None else FindNearestHitInScene ray

        match hit with
        | None -> NoIllumination
        | Some(time, point, normal, material, isEntering) -> 
            let CalculateLightIlluminationAtThisPoint = CalculateLightIllumination material point normal -ray.Direction
            let lightingIllumination = IlluminationFromAllLights scene material point normal ray

            let reflectedRay = ReflectRay ( time, ray, normal )
            let reflectedIlluminationTree = BuildLightRayTree scene (numberOfReflections - 1) reflectedRay

            let (firstMediumIndex, secondMediumIndex) = if isEntering then (1.0, material.RefractionIndex) else (material.RefractionIndex, 1.0 )
            let refractedIlluminationTree = match RefractRay material ( time, ray, normal, isEntering) with
                                            | None -> NoIllumination
                                            | Some(r) -> BuildLightRayTree scene (numberOfReflections - 1) r

            let intersection = { At = point; Normal = normal; Material = material; Illumination = lightingIllumination }
            IlluminationSource( intersection, reflectedIlluminationTree, refractedIlluminationTree )

            
            