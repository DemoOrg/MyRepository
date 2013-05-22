﻿open Matrix
open Ray
open Point
open Vector
open Shape
open Sphere
open Plane
open Color
open Light
open Material
open BRDF
open Scene
open System.Threading
open System.Threading.Tasks
open System.Timers
open RayTracer
open PhotonMapper
open KDTree


let xResolution = 256
let yResolution = 256
let colors = Color.ByName
let black = colors.["Black"]

let GetCameraRay (u: int) (v: int ) =
    let center = Vector3( 0., 0., -8. )
    let xmin = -3
    let xmax = 3
    let ymin = -3
    let ymax = 3

    let xDelta = float( xmax - xmin ) / float(xResolution )
    let yDelta = float( ymax - ymin ) / float( yResolution )
    let xPos = float(xmin) + float(u) * xDelta
    let yPos = float(ymax) - float(v) * yDelta
    let viewPoint = Vector3( xPos, yPos, 0. )
    Ray( Point3( center.X, center.Y, center.Z ), (viewPoint - center).Normalize() )

let CreateRingOfSpheres numberOfSpheres =
    let angleBetweenSpheres = 2. * System.Math.PI / float(numberOfSpheres)
    let angleBetweenSpheresInDegrees = 180. / System.Math.PI * angleBetweenSpheres
    let distanceFromCenter = 1. / System.Math.Sin ( angleBetweenSpheres / 2.0 )
    let translate = Matrix.Translate( 0., distanceFromCenter, 0. )

    let cookTorranceMaterial = new MaterialFactory( Lambertian, CookTorrance 0.1 2.1 )
    List.init numberOfSpheres ( fun i -> new Sphere( Matrix.Scale( 0.3, 0.3, 0.3 ) * Matrix.RotateX( 120.0 ) * Matrix.RotateZ(angleBetweenSpheresInDegrees * float(i)) * translate, cookTorranceMaterial.CreateMaterial( colors.["Red"], 
                                                      colors.["CornflowerBlue"], 0.3, 1.76 ) ) :> IShape )

[<EntryPoint>]
let main argv = 
    
    let l = new Light(Point3( 0., 8., 0. ), colors.["White"] )
    let l2 = new Light(Point3( 1., 2., -7. ), colors.["Aquamarine"] )
    let lightSet = [ l; l2 ]

    let cookTorranceMaterial = new MaterialFactory( Lambertian, CookTorrance 0.1 2.1 )
    let phong20Material = new MaterialFactory( Lambertian, Phong 20. )
    let phong150Material = new MaterialFactory( Lambertian, Phong 150.0 )
    let phong400Material = new MaterialFactory( Lambertian, Phong 400.0 )
    let phong600Material = new MaterialFactory( Lambertian, Phong 600.0 )

    let shapes = [   
                    new Sphere( Matrix.Scale( 1., 1., 1. ) * Matrix.Translate( 0., 0.0, 0.0 ), 
                                cookTorranceMaterial.CreateMaterial( 0.8 * colors.["CornflowerBlue"], 
                                 colors.["Red"], 0.2, 1.01 )) :> IShape;

                    new Plane( Matrix.Translate( 0., -1., 0.) * Matrix.Scale( 50., 50., 50. ) * Matrix.RotateX( 10.0 ), 
                               phong400Material.CreateMaterial( colors.["Green"], 
                                colors.["Green"], 0.2, 0. ) ) :> IShape;

//                    new Plane(  Matrix.RotateY(45.0) * Matrix.Translate( 0., 0., 5.) * Matrix.Scale( 20., 20., 20. ) * Matrix.RotateX( -90.0 ), 
//                                phong600Material.CreateMaterial( colors.["Blue"], 
//                                 colors.["Blue"], 1., 0.) ) :> IShape 
                ]
    //let shapes = List.append shapes (CreateRingOfSpheres 15)
    let scene = new Scene( lightSet, shapes )
   
    printfn "Building Photon Map"
    let photonList = BuildListOfPhotons 50000 scene l
    printfn "Photons: %d" ( List.length photonList )
    let photonMap = BuildKdTree (photonList |> List.toArray ) 0
    let flattenedMap = FlattenKdTree photonMap

    let linearSearch (t: Point3) = flattenedMap |> List.filter( fun (p,c) -> (t-p).LengthSquared() < 0.005 )
    let kdSearch (t: Point3) = FindAllPointsNearPoint2 photonMap t 0.05 0

    let rec CalculatePhotonIllumination (illuminationTree: IlluminationTree) =
        match illuminationTree with
        | NoIllumination -> black
        | IlluminationSource(hit, reflected, refracted ) ->
                let photons = kdSearch hit.Point
                let pc =  if photons.Length > 0 then 
                               1.0 / float( photons.Length ) * ( photons |> List.reduce ( fun acc color -> acc + color ))
                          else
                               Color( 0., 0., 0. )
                let i = pc

                let percentFromRefraction = 1. - hit.Material.Reflectivity
                i
                + hit.Illumination 
                + hit.Material.Reflectivity * ( CalculatePhotonIllumination reflected ) 
                + percentFromRefraction * (CalculatePhotonIllumination refracted )

    let ColorPixel u v =
        let ray = GetCameraRay u v
        CalculatePhotonIllumination (BuildLightRayTree scene 5 ray)

    
    let ColorXRow v =
        let mutable pixels = []
        for u = 0 to xResolution-1 do 
            let shade = ColorPixel u v
            pixels <- (u, v, shade) :: pixels
        pixels

    printfn "Ray Tracing"
    let startTime = System.DateTime.Now
    let pixelColors = ref []
    let rowCount = ref 0
    let _ = Parallel.For( 0, yResolution - 1, new System.Action<int>( fun y -> let row = ColorXRow y
                                                                               lock rowCount ( fun () -> rowCount := !rowCount + 1
                                                                                                         printfn "%d" !rowCount )
                                                                               lock pixelColors ( fun () -> pixelColors := row :: !pixelColors )  ) )

    let bmp = new System.Drawing.Bitmap( xResolution, yResolution )
    !pixelColors |> List.iter ( fun pl -> pl |> List.iter ( fun p -> let (u, v, color) = p 
                                                                     bmp.SetPixel(u, v, color.GetSystemColor() ) ) )

    System.Console.Write "Save File Name: "
    let fileName = System.Console.ReadLine ()
    bmp.Save( fileName )
    let endTime = System.DateTime.Now
    let duration = (endTime - startTime).TotalSeconds
    printfn "Parallel Duration: %f" duration
    
    0 // return an integer exit code